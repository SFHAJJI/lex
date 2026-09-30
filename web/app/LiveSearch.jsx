// The search screen, asked of the live API.
//
// The server renders the form and the idle state, and the browser asks only when the reader submits,
// so hydration changes nothing the server rendered and each answer arrives as a state change. What
// an answer means is decided elsewhere: `scripts/live-search.mjs` builds the request and maps the
// answer to a view state, `scripts/search-answer.mjs` reads the answer, and `RefusalCard` lays out a
// refusal. This file lays the view out and marks the state on the answer (`data-answer-state`) so a
// browser run can wait for it rather than for a clock.
//
// A hit carries no text snippet (the answer holds none), so a row says which article of which work
// and version matched, in which lane, and the permalink that pins it. The permalink is printed, not
// linked: this origin serves no reading page for it yet.
//
// The form's controls carry no `name`: a submit the browser performs itself (before the bundle has
// hydrated the page, or without it) then sends nothing, so the phrase never reaches the address bar,
// the history or a referrer. Only the hydrated screen sends it, in a request body.

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { quotationLanguageTag } from '../scripts/live-reading.mjs';
import {
  LIVE_SEARCH_IDLE,
  SEARCH_LANGUAGES,
  SEARCH_QUERY_MAX,
  createSearchSession,
} from '../scripts/live-search.mjs';
import { liveChrome } from '../scripts/live-chrome.mjs';

/** The forms' labels and buttons, from the interface copy table. */
const FORM = liveChrome().form;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_SEARCH_IDLE });

const LANE_LABEL = Object.freeze({ strict: 'exact phrase', relaxed: 'every word' });

/** A platform phrase as the start of a sentence: its first letter capitalised, a full stop after it. */
function asSentence(text) {
  return `${text.charAt(0).toUpperCase()}${text.slice(1)}.`;
}

function laneCount(count) {
  return count === null ? 'not counted' : String(count);
}

export function SearchWorkResolution({ resolution }) {
  // A matched title is the publisher's text, marked in the language that title is written in, which the
  // card names: the resolver searches every language's titles, so the search's language is not it (review
  // of #797).
  if (resolution.outcome === 'one_work') {
    return (
      <p data-work-resolution="one_work">
        The phrase names the work “<span lang={quotationLanguageTag(resolution.work.matchedTitleLanguage)}>{resolution.work.matchedTitle}</span>” ({resolution.work.workIdentifier}).
      </p>
    );
  }
  if (resolution.outcome === 'several_candidates') {
    return (
      <div data-work-resolution="several_candidates">
        <p>The phrase matches the titles of several works:</p>
        <ul>
          {resolution.candidates.map((card) => (
            <li key={card.workIdentifier}>“<span lang={quotationLanguageTag(card.matchedTitleLanguage)}>{card.matchedTitle}</span>” ({card.workIdentifier})</li>
          ))}
        </ul>
      </div>
    );
  }
  if (resolution.outcome === 'no_titles_held') {
    return (
      <p data-work-resolution="no_titles_held">
        This index holds no work titles, so the phrase was matched against article text only.
      </p>
    );
  }
  return null;
}

function AmbiguousWorks({ works, date }) {
  if (works.length === 0) return null;
  return (
    <section data-ambiguous-works={works.length}>
      <h2>Works with several versions on {date}</h2>
      <p>These works have more than one version that applies on that date, so none is chosen and none contributes a hit.</p>
      <ul>
        {works.map((work) => (
          <li key={work.workKey}>
            {work.workKey}:{' '}
            {work.candidates.map((candidate, index) => (
              <span key={candidate}>
                {index > 0 ? ', ' : ''}
                <code>{candidate}</code>
              </span>
            ))}
          </li>
        ))}
      </ul>
    </section>
  );
}

function Hit({ hit }) {
  return (
    <li data-lane={hit.lane}>
      <strong>{hit.publisherId}</strong> in {hit.workKey}, version of {hit.applicabilityDate}{' '}
      <span className="badge">{LANE_LABEL[hit.lane]}</span>
      <br />
      <code>{hit.permalink}</code>
    </li>
  );
}

/** One search view, laid out: the hits and what they are, or the no-hit answer. */
export function SearchResultsView({ view, onNextPage }) {
  const { population } = view;
  return (
    <>
      <p data-population="">
        “{view.query}” in {view.language}: {laneCount(population.strictHits)} with the exact phrase,{' '}
        {laneCount(population.relaxedHits)} with every word, in {population.worksWithHits}{' '}
        {population.worksWithHits === 1 ? 'work' : 'works'}.
      </p>
      <SearchWorkResolution resolution={view.workResolution} />
      <AmbiguousWorks works={view.ambiguousWorks} date={view.date} />
      {view.hits.length === 0 ? (
        view.searchableTextHeld ? (
          <p data-no-hit="">No article of the text this server holds contains “{view.query}”. {asSentence(view.matching)}</p>
        ) : (
          <p data-no-hit="">
            This index holds no searchable text in {view.language}; it holds text in {view.searchableLanguages.join(', ')}.
          </p>
        )
      ) : (
        <>
          <p>{asSentence(view.pageIs)} {asSentence(view.hitUnit)}</p>
          <ol className="hits">
            {view.hits.map((hit) => (
              <Hit key={`${hit.stateSha256}.${hit.articleIdentitySha256}`} hit={hit} />
            ))}
          </ol>
        </>
      )}
      {view.truncated && onNextPage ? (
        <button type="button" onClick={() => onNextPage(view.continueAfter)}>
          Next page
        </button>
      ) : null}
    </>
  );
}

/** One view state, laid out: the results, the refusal card, or the sentence a state carries. */
export function SearchAnswerView({ outcome, onNextPage }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        <SearchResultsView view={outcome.view} onNextPage={onNextPage} />
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
 * The live screen: a form, and the state of the last search asked. A search is asked when the
 * reader submits (never while rendering, never on its own); the session (`createSearchSession`)
 * cancels the one in flight when another is asked, and unmounting cancels whatever is left.
 * `contract` and `fetchImpl` must be stable for the screen's life.
 */
export function LiveSearch({ contract, fetchImpl }) {
  const [query, setQuery] = useState('');
  const [language, setLanguage] = useState(SEARCH_LANGUAGES[0].code);
  const [outcome, setOutcome] = useState(IDLE);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createSearchSession({ contract, fetchImpl, onOutcome: setOutcome });
  }

  useEffect(() => () => session.current.cancel(), []);

  return (
    <div>
      <form
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          session.current.ask({ query, language });
        }}
      >
        <label>
          {FORM.phrase}{' '}
          <input
            type="search"
            value={query}
            maxLength={SEARCH_QUERY_MAX}
            autoComplete="off"
            onChange={(event) => setQuery(event.target.value)}
          />
        </label>{' '}
        <label>
          {FORM.language}{' '}
          <select value={language} onChange={(event) => setLanguage(event.target.value)}>
            {SEARCH_LANGUAGES.map((offered) => (
              <option key={offered.code} value={offered.code}>
                {offered.label}
              </option>
            ))}
          </select>
        </label>{' '}
        <button type="submit">{FORM.submit.search}</button>
      </form>
      <SearchAnswerView outcome={outcome} onNextPage={(after) => session.current.next(after)} />
    </div>
  );
}
