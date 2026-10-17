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
// and version matched, in which lane, and links to this site's reading of it at that article (the
// permalink that pins it, `permalinks.mjs`). The row prints no digest (UX spec section 2: hit rows carry no
// hashes); the reading shows the pin.
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
//
// The screen has two forms of one form (`variant`). The home page's (R47, R52) is the one box, with its
// placeholder, a date field that starts at today, the calendar date in Luxembourg (R13), and the
// language: a search across the Luxembourg works this server holds, at that date, which the line under
// it says, with the way to an EU act (the search page). The search page's form adds the optional work
// identifier, which is how an EU act is searched, and its date field starts empty (`live-search.mjs`
// says why). Below either form, before any answer, the sentence that says
// what Lex is not (R50), which the search results carry on both pages.
//
// Exact references first (the plan's item 4.2): the platform reads the query as an exact reference before any word is
// searched. A reference it holds answers with the address its act opens at; the screen says what it recognised (the act,
// the article and the date, printed back in words) and the address it opens, then goes there. A reference that opens
// nothing says why, and a refusal of one says what was recognised above its card, so the reader sees what was read.

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { FailureFrame, LiveAnswer, Permalink, Say, StatusSentence, inEnglish, refusalCardCopyFor } from './LiveAnswer.jsx';
import { quotationLanguageTag } from '../scripts/live-reading.mjs';
import {
  LIVE_SEARCH_IDLE,
  SEARCH_LANGUAGES,
  SEARCH_QUERY_MAX,
  createSearchSession,
  longDate,
  todayInLuxembourg,
} from '../scripts/live-search.mjs';
import { CHROME_LOCALE, ENGLISH_LANG, countedEntry, fillText, liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { articlePermalink, permalinkHref } from '../scripts/permalinks.mjs';

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
                    <Permalink value={candidate} />
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
      <a href={permalinkHref(hit.permalink, CHROME_LOCALE)}>
        <Say
          template={COPY.euHit}
          values={{ heading: <strong lang={quotationLanguageTag(hit.language)}>{hit.heading}</strong>, celex: hit.celex, date: hit.wordingDate }}
        />
      </a>{' '}
      <span className="badge">{COPY.lane[hit.lane]}</span>
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
          values={{ celex: wording.celex, language: view.language, date: wording.wordingDate, permalink: <Permalink value={wording.permalink} /> }}
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
      <a href={permalinkHref(articlePermalink(hit.permalink, hit.publisherId), CHROME_LOCALE)}>
        <Say template={COPY.hit} values={{ article: <strong>{hit.publisherId}</strong>, work: hit.workKey, date: hit.applicabilityDate }} />
      </a>{' '}
      <span className="badge">{COPY.lane[hit.lane]}</span>
    </li>
  );
}

/**
 * The date a Luxembourg search was asked at, as its answer echoes it (R13: printed), or that none was: then every held
 * version was searched. An EU answer names no date; its wording's date is said with the wording (`EuropeWording`).
 */
function SearchedOn({ view }) {
  if (view.publisher === 'eu-eurlex') return null;
  return (
    <p data-searched-on={view.date ?? ''}>
      {view.date === null ? COPY.undated : <Say template={COPY.dated} values={{ date: view.date }} />}
    </p>
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
      <SearchedOn view={view} />
      {view.referenceReason === 'title_not_held' ? <p data-searched-as-words="title_not_held">{COPY.titleNotHeld}</p> : null}
      <SearchWorkResolution resolution={view.workResolution} />
      {europe ? <EuropeWording view={view} /> : <AmbiguousWorks works={view.ambiguousWorks} date={view.date} />}
      {view.hits.length === 0 ? (
        <>
          {view.searchableTextHeld ? (
            <p data-no-hit="">
              {/* With no work named, a search is of Luxembourg law alone, and says so. */}
              <Say template={view.identifier === null ? COPY.noHitLuxembourg : COPY.noHit} values={{ query: view.query }} /> {inEnglish(asSentence(view.matching))}
            </p>
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

/**
 * What an exact reference was read as, for the line that says so: from an answer's `recognised`, or from a refusal's
 * `recognised_*` members. The act is named by its held title when the index resolved one, else as the platform read it.
 */
export function recognitionOf(outcome) {
  if (outcome.state === 'opens' || outcome.state === 'not_opened') {
    const { recognised, referenceKind } = outcome.view;
    return {
      kind: referenceKind,
      act: recognised.act.title ?? recognised.act.identifier,
      actLanguage: recognised.act.title === null ? null : recognised.act.titleLanguage,
      article: recognised.article === null
        ? null
        : recognised.article.part === null ? recognised.article.number : `${recognised.article.number}, ${recognised.article.part}`,
      date: recognised.date?.date ?? null,
      dateSource: recognised.date?.source ?? null,
    };
  }
  const recognition = outcome.recognition ?? null;
  if (recognition === null) return null;
  return {
    kind: recognition.kind,
    act: recognition.title ?? recognition.act,
    actLanguage: null,
    article: recognition.article,
    date: recognition.date,
    dateSource: recognition.dateSource,
  };
}

/**
 * The line that says what the reference was read as: the act, the article and the date as read, the date in words, so
 * 15/03/2021 is printed back as 15 March 2021. A date the reference does not name (today in Luxembourg, or the search's)
 * is said apart, as the date read.
 */
export function Recognition({ recognition }) {
  if (recognition === null) return null;
  const { act, actLanguage, article, date, dateSource } = recognition;
  const named = date !== null && (dateSource === 'citation' || dateSource === 'identifier');
  const template = article === null
    ? (named ? COPY.recognisedDate : COPY.recognised)
    : (named ? COPY.recognisedArticleDate : COPY.recognisedArticle);
  const values = {
    act: actLanguage === null ? act : <span lang={quotationLanguageTag(actLanguage)}>{act}</span>,
    ...(article === null ? {} : { article }),
    ...(named ? { date: longDate(date) } : {}),
  };
  return (
    <div data-recognised={recognition.kind}>
      <p><Say template={template} values={values} /></p>
      {date !== null && !named ? (
        <p data-date-read={dateSource}>
          <Say template={dateSource === 'today_in_luxembourg' ? COPY.recognisedToday : COPY.recognisedSearchDate} values={{ date: longDate(date) }} />
        </p>
      ) : null}
    </div>
  );
}

/**
 * The acts a reference may name, none chosen (the review of #974, F2): each one this site holds is a link to it, named by its
 * title (else its address); each one only the publisher's catalogue names is said by its title (else its work IRI), with
 * the note that this site holds no version of it.
 */
export function ReferenceCandidates({ candidates }) {
  if (candidates === null || candidates === undefined || candidates.length === 0) return null;
  return (
    <ul data-reference-candidates={candidates.length}>
      {candidates.map((candidate) => {
        // A title is the publisher's text, marked in the language it is written in, as the work resolution marks one.
        const act = candidate.title === null
          ? <code>{candidate.identifier}</code>
          : candidate.titleLanguage === null
            ? candidate.title
            : <span lang={quotationLanguageTag(candidate.titleLanguage)}>{candidate.title}</span>;
        if (candidate.href !== null) {
          return <li key={candidate.identifier} data-candidate-held="true"><a href={candidate.href}>{act}</a></li>;
        }
        if (candidate.held === false) {
          return (
            <li key={candidate.identifier} data-candidate-held="false">
              <Say template={COPY.candidateNotHeld} values={{ act }} />
            </li>
          );
        }
        return <li key={candidate.identifier}><code>{candidate.identifier}</code></li>;
      })}
    </ul>
  );
}

/** The act the publisher's catalogue names, which this site holds no version of: said under an absent act's card. */
function CatalogueWorks({ recognition }) {
  const works = recognition?.catalogueWorks ?? null;
  if (works === null || works.length === 0) return null;
  return (
    <p data-catalogue-works={works.length}>
      <Say
        template={COPY.catalogueNamesIt}
        values={{ works: works.map((work, index) => <span key={work}>{index > 0 ? ', ' : ''}<code>{work}</code></span>) }}
      />
    </p>
  );
}

/** A reference that opens its act: what was read, and the address it opens, which the screen goes to. */
function ReferenceOpens({ outcome }) {
  const { opens } = outcome.view;
  return (
    <>
      <Recognition recognition={recognitionOf(outcome)} />
      <p role="status" data-opens={opens.addressKind}>
        <Say
          template={opens.page === 'dossier' ? COPY.opensDossier : COPY.opensReading}
          values={{ link: <a href={outcome.href}><code>{opens.address}</code></a> }}
        />
      </p>
    </>
  );
}

/** A reference that opens nothing: what was read, and why nothing was opened and no word searched. */
function ReferenceNotOpened({ outcome }) {
  const { recognised, reason } = outcome.view;
  return (
    <>
      <Recognition recognition={recognitionOf(outcome)} />
      <p role="status" data-not-opened={reason}>
        {reason === 'two_dates_disagree'
          ? COPY.notOpenedTwoDates
          : <Say template={COPY.notOpenedDate} values={{ date: recognised.date?.asTyped ?? recognised.text }} />}
      </p>
    </>
  );
}

/** One view state, laid out: the results, the act a reference opens, the refusal card, or the sentence a state carries. */
export function SearchAnswerView({ outcome, onNextPage, onRetry }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        <SearchResultsView view={outcome.view} onNextPage={onNextPage} />
      </section>
    );
  }

  if (outcome.state === 'opens') {
    return (
      <section data-answer-state="opens">
        <ReferenceOpens outcome={outcome} />
      </section>
    );
  }

  if (outcome.state === 'not_opened') {
    return (
      <section data-answer-state="not_opened">
        <ReferenceNotOpened outcome={outcome} />
      </section>
    );
  }

  if (outcome.state === 'refusal' && outcome.card) {
    return (
      <section data-answer-state="refusal">
        <FailureFrame outcome={outcome} onRetry={onRetry}>
          <Recognition recognition={recognitionOf(outcome)} />
          <RefusalCard code={outcome.code} sentence={outcome.sentence} payload={outcome.payload} copy={refusalCardCopyFor(outcome.context)} />
          <ReferenceCandidates candidates={outcome.candidates} />
          <CatalogueWorks recognition={outcome.recognition} />
        </FailureFrame>
      </section>
    );
  }

  return (
    <section data-answer-state={outcome.state}>
      <FailureFrame outcome={outcome} onRetry={onRetry}>
        {outcome.state === 'refusal' ? <Recognition recognition={recognitionOf(outcome)} /> : null}
        <StatusSentence outcome={outcome} />
      </FailureFrame>
    </section>
  );
}

/** The two forms of the search form: the home page's one box, and the search page's with a work identifier. */
export const SEARCH_VARIANTS = Object.freeze(['home', 'search']);

/**
 * The id of the line that says today beside the date field: a page holds one search form, and an id React mints
 * (`useId`) is minted from the tree's root, which the server's render and the browser's hydration of the screen's root do
 * not share.
 */
const TODAY_HINT = 'search-today';

/**
 * The live screen: a form, and the state of the last search asked. A search is asked when the
 * reader submits (never while rendering, never on its own); the session (`createSearchSession`)
 * cancels the one in flight when another is asked, and unmounting cancels whatever is left.
 * `contract` and `fetchImpl` must be stable for the screen's life.
 *
 * `variant` is `home` (the one box; the date starts at today) or `search` (with the work identifier;
 * the date starts empty). `today` is today's date in Luxembourg as the server said it when it rendered
 * the page (the home page's facts), or null: then this browser's clock says it, once hydrated, by the
 * same coded rule (`todayInLuxembourg`), and a home page's date field takes it unless the reader has
 * already written in the field. What the field holds when the reader submits is what is sent.
 */
export function LiveSearch({ contract, fetchImpl, variant = 'search', today = null }) {
  if (!SEARCH_VARIANTS.includes(variant)) throw new Error(`the search form is the home page's or the search page's, not ${JSON.stringify(variant)}`);
  const home = variant === 'home';
  const [query, setQuery] = useState('');
  const [language, setLanguage] = useState(SEARCH_LANGUAGES[0].code);
  const [identifier, setIdentifier] = useState('');
  const [knownToday, setKnownToday] = useState(today);
  const [date, setDate] = useState(home && today !== null ? today : '');
  const dateWritten = useRef(false);
  const [outcome, setOutcome] = useState(IDLE);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createSearchSession({ contract, fetchImpl, onOutcome: setOutcome });
  }

  useEffect(() => () => session.current.cancel(), []);

  // A reference the platform holds opens its act: the screen goes to the address, under this page's language, once the
  // answer says what was read (the line stays in the page this one leaves behind).
  useEffect(() => {
    if (outcome.state === 'opens') window.location.assign(outcome.href);
  }, [outcome]);

  // Today, from this browser's clock, when no server said it: after hydration, so the page the server rendered is the
  // page hydrated, and never the build's time.
  useEffect(() => {
    if (knownToday !== null) return;
    const now = todayInLuxembourg();
    if (now === null) return;
    setKnownToday(now);
    if (home && !dateWritten.current) setDate((current) => (current === '' ? now : current));
  }, []);

  // Today, said beside the date field: under it on the search page; on the home page, whose date and language share a
  // row, under the two.
  const todayHint = knownToday === null ? null : (
    <p id={TODAY_HINT} className="field-hint" data-today={knownToday}>
      {fillText(FORM.today, { date: knownToday })}
    </p>
  );

  return (
    <div className={home ? 'search-home' : undefined}>
      <form
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          session.current.ask({ query, language, identifier: home ? '' : identifier, date });
        }}
      >
        <label>
          {FORM.phrase}{' '}
          <input
            type="search"
            value={query}
            placeholder={COPY.placeholder}
            maxLength={SEARCH_QUERY_MAX}
            autoComplete="off"
            onChange={(event) => setQuery(event.target.value)}
          />
        </label>{' '}
        {home ? null : (
          <>
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
          </>
        )}
        <label>
          {FORM.date}{' '}
          <input
            type="text"
            inputMode="numeric"
            placeholder={FORM.datePlaceholder}
            value={date}
            autoComplete="off"
            data-search-date=""
            aria-describedby={knownToday === null ? undefined : TODAY_HINT}
            onChange={(event) => {
              dateWritten.current = true;
              setDate(event.target.value);
            }}
          />
        </label>{' '}
        {home ? null : todayHint}
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
        {home ? todayHint : null}
        <button type="submit">{FORM.submit.search}</button>
      </form>
      <p className="not-advice" data-not-advice="">{liveChrome().site.notAdvice}</p>
      {home ? (
        <p className="search-scope" data-search-scope="">
          <Say template={COPY.searchScope} values={{ link: <a href={livePath('search.html')}>{COPY.searchPage}</a> }} />
        </p>
      ) : null}
      <LiveAnswer>
        <SearchAnswerView outcome={outcome} onNextPage={(after) => session.current.next(after)} onRetry={() => session.current.retry()} />
      </LiveAnswer>
    </div>
  );
}
