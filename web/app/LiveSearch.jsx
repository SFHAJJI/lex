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
// An EU answer (one named EU work, `view.publisher` "eu-eurlex") is laid out with its own words: the
// one wording the hits are in, its date and its permalink, said once above the hits; each hit's
// heading, CELEX and wording date, never called a version or an applicability date, since the date
// is the one the publisher's Formex package gives the act (the answer's `date_semantics`, shown as
// sent); and what the search does not cover, as the answer lists it.
//
// The form's controls carry no `name`: a submit the browser performs itself (before the bundle has
// hydrated the page, or without it) then sends nothing, so the phrase never reaches the address bar,
// the history or a referrer. Only the hydrated screen sends it, in a request body.

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { LiveAnswer, Say, StatusSentence, inEnglish, refusalCardCopyFor } from './LiveAnswer.jsx';
import { quotationLanguageTag } from '../scripts/live-reading.mjs';
import {
  LIVE_SEARCH_IDLE,
  SEARCH_LANGUAGES,
  SEARCH_QUERY_MAX,
  createSearchSession,
} from '../scripts/live-search.mjs';
import { ENGLISH_LANG, countedEntry, liveChrome } from '../scripts/live-chrome.mjs';

/** The forms' labels and buttons, and this screen's sentences, from the interface copy table. */
const FORM = liveChrome().form;
const COPY = liveChrome().search;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_SEARCH_IDLE });

/** A platform phrase as the start of a sentence: its first letter capitalised, a full stop after it. */
function asSentence(text) {
  return `${text.charAt(0).toUpperCase()}${text.slice(1)}.`;
}

function laneCount(count) {
  return count === null ? COPY.notCounted : String(count);
}

export function SearchWorkResolution({ resolution }) {
  // A matched title is the publisher's text, marked in the language that title is written in, which the
  // card names: the resolver searches every language's titles, so the search's language is not it (review
  // of #797).
  const titled = (card) => ({
    title: <span lang={quotationLanguageTag(card.matchedTitleLanguage)}>{card.matchedTitle}</span>,
    identifier: card.workIdentifier,
  });
  if (resolution.outcome === 'one_work') {
    return (
      <p data-work-resolution="one_work">
        <Say template={COPY.namesWork} values={titled(resolution.work)} />
      </p>
    );
  }
  if (resolution.outcome === 'several_candidates') {
    return (
      <div data-work-resolution="several_candidates">
        <p>{COPY.severalWorks}</p>
        <ul>
          {resolution.candidates.map((card) => (
            <li key={card.workIdentifier}><Say template={COPY.candidate} values={titled(card)} /></li>
          ))}
        </ul>
      </div>
    );
  }
  if (resolution.outcome === 'no_titles_held') {
    return <p data-work-resolution="no_titles_held">{COPY.noTitlesHeld}</p>;
  }
  return null;
}

function AmbiguousWorks({ works, date }) {
  if (works.length === 0) return null;
  return (
    <section data-ambiguous-works={works.length}>
      <h2><Say template={COPY.ambiguousHeading} values={{ date }} /></h2>
      <p>{COPY.ambiguousNote}</p>
      <ul>
        {works.map((work) => (
          <li key={work.workKey}>
            <Say
              template={COPY.ambiguousWork}
              values={{
                work: work.workKey,
                candidates: work.candidates.map((candidate, index) => (
                  <span key={candidate}>
                    {index > 0 ? ', ' : ''}
                    <code>{candidate}</code>
                  </span>
                )),
              }}
            />
          </li>
        ))}
      </ul>
    </section>
  );
}

function EuropeHit({ hit }) {
  // The heading is the publisher's text, marked in the language of the wording it is in.
  return (
    <li data-lane={hit.lane}>
      <Say
        template={COPY.euHit}
        values={{ heading: <strong lang={quotationLanguageTag(hit.language)}>{hit.heading}</strong>, celex: hit.celex, date: hit.wordingDate }}
      />{' '}
      <span className="badge">{COPY.lane[hit.lane]}</span>
      <br />
      <code>{hit.permalink}</code>
    </li>
  );
}

/** The one wording an EU answer's hits are in, said once above them, with what its date means. */
function EuropeWording({ view }) {
  const { wording } = view;
  if (wording === null) return null;
  return (
    <>
      <p data-pinned-wording={wording.wordingSha256}>
        <Say
          template={COPY.euWording}
          values={{ celex: wording.celex, language: view.language, date: wording.wordingDate, permalink: <code>{wording.permalink}</code> }}
        />
      </p>
      <p data-date-semantics="" lang={ENGLISH_LANG}>{asSentence(view.dateSemantics)}</p>
    </>
  );
}

/** What an EU search does not cover, as the answer lists it: the platform's own items and reasons. */
function NotCovered({ rows }) {
  if (rows.length === 0) return null;
  return (
    <section data-not-held={rows.length}>
      <h2>{COPY.notHeldHeading}</h2>
      <ul>
        {rows.map((row) => (
          <li key={row.item}><Say template={liveChrome().common.notHeldRow} values={{ item: row.item, reason: inEnglish(row.reason) }} /></li>
        ))}
      </ul>
    </section>
  );
}

function Hit({ hit }) {
  return (
    <li data-lane={hit.lane}>
      <Say template={COPY.hit} values={{ article: <strong>{hit.publisherId}</strong>, work: hit.workKey, date: hit.applicabilityDate }} />{' '}
      <span className="badge">{COPY.lane[hit.lane]}</span>
      <br />
      <code>{hit.permalink}</code>
    </li>
  );
}

/** One search view, laid out: the hits and what they are, or the no-hit answer. */
export function SearchResultsView({ view, onNextPage }) {
  const { population } = view;
  const europe = view.publisher === 'eu-eurlex';
  return (
    <>
      <p data-population="">
        <Say
          template={countedEntry(COPY.population, population.worksWithHits)}
          values={{
            query: view.query,
            language: view.language,
            strict: laneCount(population.strictHits),
            relaxed: laneCount(population.relaxedHits),
            works: population.worksWithHits,
          }}
        />
      </p>
      <SearchWorkResolution resolution={view.workResolution} />
      {europe ? <EuropeWording view={view} /> : <AmbiguousWorks works={view.ambiguousWorks} date={view.date} />}
      {view.hits.length === 0 ? (
        <>
          {view.searchableTextHeld ? (
            <p data-no-hit=""><Say template={COPY.noHit} values={{ query: view.query }} /> {inEnglish(asSentence(view.matching))}</p>
          ) : (
            <p data-no-hit="">
              <Say template={COPY.noText} values={{ language: view.language, languages: view.searchableLanguages.join(', ') }} />
            </p>
          )}
          {/* A search with no hit is not evidence that the law does not exist, said as the refusal card says it (journey J2). */}
          <p data-absence-note="">{liveChrome().refusalCard.absenceNote}</p>
        </>
      ) : (
        <>
          <p lang={ENGLISH_LANG}>{asSentence(view.pageIs)} {asSentence(view.hitUnit)}</p>
          <ol className="hits">
            {view.hits.map((hit) => (europe
              ? <EuropeHit key={hit.articleIdentitySha256} hit={hit} />
              : <Hit key={`${hit.stateSha256}.${hit.articleIdentitySha256}`} hit={hit} />
            ))}
          </ol>
        </>
      )}
      {europe ? <NotCovered rows={view.notHeld} /> : null}
      {view.truncated && onNextPage ? (
        <button type="button" onClick={() => onNextPage(view.continueAfter)}>
          {COPY.nextPage}
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
 * The live screen: a form, and the state of the last search asked. A search is asked when the
 * reader submits (never while rendering, never on its own); the session (`createSearchSession`)
 * cancels the one in flight when another is asked, and unmounting cancels whatever is left.
 * `contract` and `fetchImpl` must be stable for the screen's life.
 */
export function LiveSearch({ contract, fetchImpl }) {
  const [query, setQuery] = useState('');
  const [language, setLanguage] = useState(SEARCH_LANGUAGES[0].code);
  const [identifier, setIdentifier] = useState('');
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
          session.current.ask({ query, language, identifier });
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
          {FORM.workIdentifierOptional}{' '}
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
            {SEARCH_LANGUAGES.map((offered) => (
              <option key={offered.code} value={offered.code}>
                {offered.label}
              </option>
            ))}
          </select>
        </label>{' '}
        <button type="submit">{FORM.submit.search}</button>
      </form>
      <LiveAnswer>
        <SearchAnswerView outcome={outcome} onNextPage={(after) => session.current.next(after)} />
      </LiveAnswer>
    </div>
  );
}
