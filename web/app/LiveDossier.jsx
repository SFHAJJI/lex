// The dossier screen, asked of the live API.
//
// The server renders the form and the idle state, and the browser asks only when the reader submits,
// so hydration changes nothing the server rendered and each answer arrives as a state change. What
// an answer means is decided elsewhere: `scripts/live-dossier.mjs` builds the request and maps the
// answer to a view state, `scripts/dossier-answer.mjs` reads it, and `RefusalCard` lays out a
// refusal. This file lays the view out and marks the state on the answer (`data-answer-state`).
//
// The form's controls carry no `name`, as the search screen's do: a submit the browser performs
// itself, before hydration or without the bundle, then sends nothing. Permalinks are printed, not
// linked: this origin serves no reading page for them yet.

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { DOSSIER_LANGUAGES, LIVE_DOSSIER_IDLE, createDossierSession } from '../scripts/live-dossier.mjs';
import { LiveAnswer, Say } from './LiveAnswer.jsx';
import { quotationLanguageTag } from '../scripts/live-reading.mjs';
import { countedEntry, liveChrome } from '../scripts/live-chrome.mjs';

/** The forms' labels and buttons, and this screen's sentences, from the interface copy table. */
const FORM = liveChrome().form;
const COPY = liveChrome().dossier;
const COMMON = liveChrome().common;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_DOSSIER_IDLE });

/** A platform phrase as a sentence: its first letter capitalised, a full stop after it unless it has one. */
function asSentence(text) {
  const capitalised = `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
  return /[.!?]$/.test(capitalised) ? capitalised : `${capitalised}.`;
}

export function DossierTitles({ titles }) {
  if (titles.length === 0) {
    return <p data-titles="none">{COPY.noTitle}</p>;
  }
  return (
    <ul data-titles={titles.length}>
      {titles.map((group) => (
        <li key={`${group.language}.${group.expressionIri}`}>
          {/* The publisher's titles are statute text, marked in their own language, apart from the
              interface's (the launch contract's statute-language line). */}
          {group.language}:{' '}
          {[
            ...group.titles.map((entry) => ({ title: entry.title, short: false })),
            ...group.shortTitles.map((entry) => ({ title: entry.title, short: true })),
          ].map((entry, index) => {
            const title = <span lang={quotationLanguageTag(group.language)}>{entry.title}</span>;
            return (
              <span key={`${index}.${entry.title}`}>
                {index === 0 ? '' : '; '}
                {entry.short ? <Say template={COPY.shortTitle} values={{ title }} /> : title}
              </span>
            );
          })}
        </li>
      ))}
    </ul>
  );
}

/** One dossier view, laid out: the work, its titles, its held states and what it does not hold. */
export function DossierView({ view }) {
  return (
    <>
      <h2>{view.workKey}</h2>
      <p>
        <Say template={COPY.heldIn} values={{ iri: <code>{view.publisherWorkIri}</code>, languages: view.availableLanguages.join(', ') }} />
      </p>
      <DossierTitles titles={view.titles} />
      <p data-history="">
        {view.language === null ? (
          <Say template={countedEntry(COPY.states, view.stateCount)} values={{ count: view.stateCount, from: view.historyBegins, to: view.latestApplicabilityDate }} />
        ) : (
          <Say template={countedEntry(COPY.statesIn, view.stateCount)} values={{ count: view.stateCount, language: view.language, from: view.historyBegins, to: view.latestApplicabilityDate }} />
        )}
      </p>
      <table>
        <thead>
          <tr>
            <th scope="col">{COMMON.language}</th>
            <th scope="col">{COMMON.appliesFrom}</th>
            <th scope="col">{COMMON.nextFrom}</th>
            <th scope="col">{COPY.articlesHeld}</th>
            <th scope="col">{COPY.articlesNotAdmitted}</th>
            <th scope="col">{COMMON.permalink}</th>
          </tr>
        </thead>
        <tbody>
          {view.states.map((state) => (
            <tr key={state.stateSha256} data-state={state.stateSha256}>
              <td>{state.language}</td>
              <td>{state.applicabilityDate}</td>
              <td>{state.nextApplicabilityDate ?? COMMON.noneHeld}</td>
              <td>{state.articleCount}</td>
              <td>{state.articlesNotAdmitted}</td>
              <td><code>{state.permalink}</code></td>
            </tr>
          ))}
        </tbody>
      </table>
      <p>{asSentence(view.articlesNotAdmittedNote)}</p>
      <h3>{COPY.notHeldHeading}</h3>
      <ul data-not-held={view.notHeld.length}>
        {view.notHeld.map((row) => (
          <li key={row.item}>
            <Say template={COMMON.notHeldRow} values={{ item: <strong>{row.item}</strong>, reason: row.reason }} />
          </li>
        ))}
      </ul>
      <p>{asSentence(view.scope)}</p>
    </>
  );
}

/** One view state, laid out: the dossier, the refusal card, or the sentence a state carries. */
export function DossierAnswerView({ outcome }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        <DossierView view={outcome.view} />
      </section>
    );
  }

  if (outcome.state === 'refusal' && outcome.card) {
    return (
      <section data-answer-state="refusal">
        <RefusalCard code={outcome.code} sentence={outcome.sentence} payload={outcome.payload} copy={liveChrome().refusalCard} />
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
 * The live screen: a form, and the state of the last dossier asked. A dossier is asked when the
 * reader submits, never while rendering; the session cancels the one in flight when another is
 * asked, and unmounting cancels whatever is left. `contract` and `fetchImpl` must be stable for the
 * screen's life.
 */
export function LiveDossier({ contract, fetchImpl }) {
  const [identifier, setIdentifier] = useState('');
  const [language, setLanguage] = useState('');
  const [outcome, setOutcome] = useState(IDLE);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createDossierSession({ contract, fetchImpl, onOutcome: setOutcome });
  }

  useEffect(() => () => session.current.cancel(), []);

  return (
    <div>
      <form
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          session.current.ask({ identifier, language });
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
          {FORM.language}{' '}
          <select value={language} onChange={(event) => setLanguage(event.target.value)}>
            <option value="">{FORM.anyLanguage}</option>
            {DOSSIER_LANGUAGES.map((offered) => (
              <option key={offered.code} value={offered.code}>
                {offered.label}
              </option>
            ))}
          </select>
        </label>{' '}
        <button type="submit">{FORM.submit.dossier}</button>
      </form>
      <LiveAnswer>
        <DossierAnswerView outcome={outcome} />
      </LiveAnswer>
    </div>
  );
}
