// The search screen, asked of the live API.
//
// The server renders the form and the idle state, and the browser asks only when the reader submits,
// so hydration changes nothing the server rendered and each answer arrives as a state change. What
// an answer means is decided elsewhere: `scripts/live-search.mjs` builds the request and maps the
// answer to a view state, `scripts/search-answer.mjs` reads the answer, and `RefusalCard` lays out a
// refusal. This file lays the view out and marks the state on the answer (`data-answer-state`) so a
// browser run can wait for it rather than for a clock.
//
// Results by act (the plan's item 4.3; R59, R21, R11): a Luxembourg answer is laid out act by act, each act's title once
// (linked to its dossier; the as-published label when the act is held only as first published) and its passages below it,
// version by version, each version dated as the publisher dates it ("applicable from", R21). A passage links to this site's
// reading at its article (the permalink that pins it, `permalinks.mjs`) and quotes the extract the answer carries, word
// for word, with the matched words marked (`<mark>`). A row prints no digest (UX spec section 2: hit rows carry no
// hashes); the reading shows the pin. The act whose title the phrase is comes first, above the passages; when several
// titles match, they are counted and listed, the first few shown and the rest one step away. Above the list, what was found
// and what was searched, every count the answer's and dated by the index's build (R11).
//
// A search with no passage shows the plain no-hit card (the plan's item 4.5; R59, R90): which searches ran (the exact
// reference, the titles, the text), that only the phrase was matched, with capitals and accents ignored (the plan's item
// 4.4), and what that means, the population searched, the publishers' own search pages as links (nothing here requests
// them), and the absence note.
//
// An EU answer (one named EU act, `view.publisher` "eu-eurlex"; the audit of 2026-10-27, gap 5) is laid out with its own
// words, wording by wording: the acknowledgement and the authenticity statement every EU text served carries (Decision 95),
// what was searched (the wordings, with no date or at the date asked) and what was not; then each wording with hits, headed
// by its date in its role, as the reading heads it (R21: the original wording by the act's Formex date, a consolidated
// wording by the publisher's consolidation date, and the dates it answers), never called a version or an applicability
// date; under it each passage, the article's heading linked to the reading at that wording, its extract quoted with the
// matched words marked, and, where an undated search showed an unchanged article once, how many other wordings hold the
// same text; and what the search does not cover, as the answer lists it.
//
// The form's controls carry no `name`: a submit the browser performs itself (before the bundle has
// hydrated the page, or without it) then sends nothing, so the phrase never reaches a query string, the
// history or a referrer. Only the hydrated screen sends it, in a request body.
//
// The search lives in the page's address after "#" (the plan's item 3.5; R109), so it can be shared: once the reader asks
// a search, the screen writes it into the fragment (`searchFragment`: the phrase, its language, the work named and the
// date), replacing the address, never adding to the history; once hydrated, it reads the fragment the page was opened at
// (and any the reader writes later) and asks that search, as the reader would (`readSearchFragment`). The fragment never
// reaches a server, and the words still go to the API in the request's body alone. A fragment written as a search that the
// form will not send is said so, repeating nothing of it, and nothing is asked. A search read from the address that opens
// an act (an exact reference) shows the link and does not go there by itself, so the browser's Back never loops through
// it. The 404 page's box keeps nothing in its address (`addressed`): it stands at an address this site has no page for.
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

import { Fragment, useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { FailureFrame, LiveAnswer, Permalink, Say, StatusSentence, inEnglish, refusalCardCopyFor } from './LiveAnswer.jsx';
import { quotationLanguageTag } from '../scripts/live-reading.mjs';
import {
  LIVE_SEARCH_IDLE,
  SEARCH_LANGUAGES,
  SEARCH_QUERY_MAX,
  TITLES_SHOWN,
  actGroups,
  builtOnOf,
  createSearchSession,
  indexOlderThanToday,
  languageName,
  longDate,
} from '../scripts/live-search.mjs';
import { readSearchFragment, searchFragment } from '../scripts/search-address.mjs';
import { todayAt } from '../scripts/initial-answer.mjs';
import { replacePageAddress } from './page-address.mjs';
import { TodayHint, useLuxembourgToday } from './Today.jsx';
import { OFFICIAL_SEARCH_PAGES } from '../scripts/live-failure.mjs';
import { CHROME_LOCALE, ENGLISH_LANG, countedEntry, fillCounted, fillText, liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { articlePermalink, permalinkHref } from '../scripts/permalinks.mjs';
import { datedByEntryIntoForce } from '../scripts/date-role.mjs';

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

/**
 * A matched title, the publisher's text marked in its own language, linked to the act's dossier when the card names the
 * work this site holds; a title of an act this site holds no version of (the index holds titles of acts it holds no state
 * of: on `derive-c`, 168 of the 1,502 titles "règlement grand-ducal" matches) is said so, as a reference's list says it.
 */
function TitleOf({ card }) {
  // The resolver searches every language's titles, so the search's language is not the title's (review of #797).
  const title = <span lang={quotationLanguageTag(card.matchedTitleLanguage)}>{card.matchedTitle}</span>;
  return typeof card.workKey !== 'string'
    ? <Say template={COPY.candidateNotHeld} values={{ act: title }} />
    : <a href={permalinkHref(`/lu-legilux/${card.workKey}`, CHROME_LOCALE)}>{title}</a>;
}

/** The sentence for the one act the title ladder names, by how its title matched: is, begins, or holds every word. */
const TITLE_MATCH = Object.freeze({ exact_normalized_title: 'titleExact', prefix: 'titleStarts', all_tokens_contained: 'titleWords' });

/**
 * The title ladder's answer, on top of the results (the plan's item 4.3): the act the phrase is the title of (or begins, or
 * holds every word of), linked to it; or, when several titles match, their count and the first few, the rest one step away.
 */
export function SearchWorkResolution({ resolution }) {
  if (resolution.outcome === 'one_work') {
    return (
      <p data-work-resolution="one_work" data-match-reason={resolution.work.matchReason}>
        <Say template={COPY[TITLE_MATCH[resolution.work.matchReason] ?? 'titleWords']} values={{ title: <TitleOf card={resolution.work} /> }} />
      </p>
    );
  }
  if (resolution.outcome === 'several_candidates') {
    const shown = resolution.candidates.slice(0, TITLES_SHOWN);
    const more = resolution.candidates.slice(TITLES_SHOWN);
    const item = (card) => <li key={`${card.workIdentifier} ${card.workKey}`}><TitleOf card={card} /></li>;
    return (
      <div data-work-resolution="several_candidates" data-candidates={resolution.candidates.length}>
        <p><Say template={countedEntry(COPY.severalTitles, resolution.candidates.length)} values={{ count: resolution.candidates.length }} /></p>
        <ul>{shown.map(item)}</ul>
        {more.length > 0 ? (
          <details data-more-titles={more.length}>
            <summary>{fillCounted(COPY.moreTitles, more.length)}</summary>
            <ul>{more.map(item)}</ul>
          </details>
        ) : null}
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
                  <span key={`${candidate}#${index}`}>
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

/** The act an EU answer is of, as its headings name it: the act whose census was searched, else the wording's own CELEX. */
function europeAct(view) {
  return view.seedCelex ?? view.wordings[0]?.celex ?? view.identifier;
}

/**
 * One EU passage: the article's heading, the publisher's text marked in the wording's language, linked to the reading at that
 * wording (the provision's permalink, which pins it), its extract, and, when an undated search showed the article's text once,
 * how many other wordings searched hold the same text (said, not dropped). A relaxed hit says its lane.
 */
function EuropePassage({ hit }) {
  return (
    <li data-lane={hit.lane}>
      <a href={permalinkHref(hit.permalink, CHROME_LOCALE)}><strong lang={quotationLanguageTag(hit.language)}>{hit.heading}</strong></a>
      {hit.lane === 'relaxed' ? <>{' '}<span className="badge">{COPY.lane.relaxed}</span></> : null}
      <Extract extract={hit.extract} language={hit.language} />
      {hit.sameWording.length > 0 ? (
        <p className="note" data-same-wording={hit.sameWording.length}>{fillCounted(COPY.euSameWording, hit.sameWording.length)}</p>
      ) : null}
    </li>
  );
}

/**
 * An EU answer's passages, wording by wording (R21): each wording with hits on the page once, headed by its date in its role
 * as the reading heads it, the dates it answers, its permalink, then its passages in the page's order.
 */
function EuropeWordings({ view }) {
  const reading = liveChrome().reading;
  const act = europeAct(view);
  return (
    <ol className="acts" data-wordings={view.wordings.length}>
      {view.wordings
        .map((wording) => ({ wording, hits: view.hits.filter((hit) => hit.publisherExpressionIri === wording.publisherExpressionIri) }))
        .filter(({ hits }) => hits.length > 0)
        .map(({ wording, hits }) => (
          <li key={wording.publisherExpressionIri} data-wording={wording.wordingDate ?? ''} data-wording-kind={wording.kind}>
            <h2 className="act-title">
              <Say
                template={wording.kind === 'consolidated_version' ? reading.europeConsolidatedHeading : reading.europeOriginalHeading}
                values={{ celex: act, language: view.language, date: wording.wordingDate }}
              />
            </h2>
            <p className="act-facts" data-wording-holds="">
              {wording.nextDate === null
                ? reading.europeLatest
                : <Say template={reading.europeHoldsUntil} values={{ date: wording.wordingDate, next: wording.nextDate }} />}
            </p>
            {wording.permalink === null ? null : <p><Permalink value={wording.permalink} /></p>}
            <ol className="passages">
              {hits.map((hit) => <EuropePassage key={hit.articleIdentitySha256} hit={hit} />)}
            </ol>
          </li>
        ))}
    </ol>
  );
}

/**
 * What an EU search's lanes matched against, every count the answer's (R11): its wordings and their articles of searchable
 * text, at the date asked or with none; and, when there are any, the wordings of the act it did not search.
 */
function EuropeSearched({ view }) {
  const { searched } = view;
  const notSearched = searched.withheldByRights + (searched.notSearched === null
    ? 0
    : searched.notSearched.withoutText + searched.notSearched.differentTexts + searched.notSearched.withoutUsableDate);
  return (
    <>
      <p data-searched={searched.wordings}>
        {fillText(COPY.euSearched, {
          wordings: fillCounted(COPY.euWordings, searched.wordings),
          celex: europeAct(view),
          language: languageName(view.language),
          articles: fillCounted(liveChrome().home.articles, searched.articles),
        })}
      </p>
      {view.seedCelex === null ? null : (
        <p data-searched-on={view.date ?? ''}>
          {view.date === null ? COPY.euUndated : <Say template={COPY.euDated} values={{ date: view.date }} />}
        </p>
      )}
      {notSearched > 0 ? <p data-not-searched={notSearched}>{fillCounted(COPY.euNotSearched, notSearched)}</p> : null}
    </>
  );
}

/** The acknowledgement and the authenticity statement every EU text served carries (Decision 95), as the platform words them. */
function EuropeStatements({ view }) {
  return (
    <>
      <p data-acknowledgement="" lang={ENGLISH_LANG}>{view.acknowledgement}</p>
      <p data-authenticity="" lang={ENGLISH_LANG}>{view.authenticity}</p>
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

/** An extract as the answer cuts it: the text word for word, each whole match marked, an elision where the text goes on. */
export function Extract({ extract, language }) {
  return (
    <p className="extract" data-extract="" data-byte-start={extract.byteStart} data-byte-end={extract.byteEnd}>
      {extract.startsText ? null : <span data-elided="before">… </span>}
      <q lang={quotationLanguageTag(language)}>
        {extract.parts.map((part, index) => (part.matched ? <mark key={index}>{part.text}</mark> : <Fragment key={index}>{part.text}</Fragment>))}
      </q>
      {extract.endsText ? null : <span data-elided="after"> …</span>}
    </p>
  );
}

/** One passage: the article, linked to its reading at that version, and its extract. A relaxed hit says its lane. */
function Passage({ hit }) {
  return (
    <li data-lane={hit.lane}>
      <a href={permalinkHref(articlePermalink(hit.permalink, hit.publisherId), CHROME_LOCALE)}><strong>{hit.publisherId}</strong></a>
      {hit.lane === 'relaxed' ? <>{' '}<span className="badge">{COPY.lane.relaxed}</span></> : null}
      <Extract extract={hit.extract} language={hit.language} />
      {/* An undated search folds one wording into one passage (S7a): the other versions that hold it are said, not dropped. */}
      {hit.sameWording?.length > 0 ? (
        <p className="note" data-same-wording={hit.sameWording.length}>{fillCounted(COPY.sameWording, hit.sameWording.length)}</p>
      ) : null}
    </li>
  );
}

/**
 * The page's hits, act by act (the plan's item 4.3): each act's title once, linked to its dossier (or its work key, when the
 * index holds no title), the as-published label when the act is held only as first published, how many passages it has in
 * the whole result, then each version's passages under that version's date in the publisher's words (R21).
 */
function ActGroups({ view }) {
  const asPublishedLabel = liveChrome().common.asPublished.label;
  return (
    <ol className="acts" data-acts={view.acts.length}>
      {actGroups(view).map(({ act, versions }) => (
        <li key={act.workKey} data-act={act.workKey}>
          <h2 className="act-title">
            {act.title === null
              ? <Say template={COPY.untitled} values={{ work: <a href={permalinkHref(act.dossier, CHROME_LOCALE)}>{act.workKey}</a> }} />
              : <a href={permalinkHref(act.dossier, CHROME_LOCALE)}><span lang={quotationLanguageTag(act.titleLanguage)}>{act.title}</span></a>}
          </h2>
          <p className="act-facts">
            {act.asPublished ? <span data-as-published-label="">{`${asPublishedLabel.charAt(0).toUpperCase()}${asPublishedLabel.slice(1)}.`} </span> : null}
            <span data-act-hits={act.hits}>{fillCounted(COPY.actPassages, act.hits)}</span>
          </p>
          {versions.map(({ stateSha256, applicabilityDate, dateRole, hits }) => (
            <section key={stateSha256} data-version={applicabilityDate}>
              <p className="version-line" data-date-role={dateRole ?? undefined}>
                {/* An act dated by its entry into force is never said as applicable from it (R21, `date-role.mjs`). */}
                <Say
                  template={datedByEntryIntoForce({ dateRole }) ? COPY.versionAsPublishedEntryIntoForce : act.asPublished ? COPY.versionAsPublished : COPY.version}
                  values={{ date: applicabilityDate }}
                />
              </p>
              <ol className="passages">
                {hits.map((hit) => <Passage key={`${hit.stateSha256}.${hit.articleIdentitySha256}`} hit={hit} />)}
              </ol>
            </section>
          ))}
        </li>
      ))}
    </ol>
  );
}

/**
 * The date a Luxembourg search was asked at, as its answer echoes it (R13: printed), or that none was: then every held
 * version was searched. An EU answer says its own, with what it searched (`EuropeSearched`).
 */
function SearchedOn({ view }) {
  if (view.publisher === 'eu-eurlex') return null;
  return (
    <p data-searched-on={view.date ?? ''}>
      {view.date === null ? COPY.undated : <Say template={COPY.dated} values={{ date: view.date }} />}
    </p>
  );
}

/** What a search found: in the strict lane alone, its passages and acts; otherwise both lanes' counts, as before. */
function Found({ view }) {
  const { population } = view;
  if (view.mode === 'strict' && view.publisher === 'eu-eurlex') {
    // An EU act's passages are counted by the wordings they are in, never as acts: one act, several wordings.
    return (
      <p data-population="">
        <Say
          template={COPY.euFound}
          values={{
            query: view.query,
            language: languageName(view.language),
            passages: fillCounted(COPY.passages, population.strictHits),
            wordings: fillCounted(COPY.euWordings, view.wordings.filter((wording) => wording.hits > 0).length),
            celex: europeAct(view),
          }}
        />
      </p>
    );
  }
  if (view.mode === 'strict') {
    return (
      <p data-population="">
        <Say
          template={COPY.found}
          values={{
            query: view.query,
            language: languageName(view.language),
            passages: fillCounted(COPY.passages, population.strictHits),
            acts: fillCounted(liveChrome().home.acts, population.worksWithHits),
          }}
        />
      </p>
    );
  }
  return (
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
  );
}

/**
 * What a Luxembourg search's lanes matched against (R11): its acts, versions and articles of searchable text, every count
 * the answer's, as of the index built on its build's date in Luxembourg, and today beside it when the index is older (R13:
 * "When the index is older, both dates are shown"; `today`, the date in Luxembourg the answer was given on); and, when
 * there are any, the versions the rights rule kept out and the acts a date could not place.
 */
function Searched({ view, today = null }) {
  const { searched } = view;
  const home = liveChrome().home;
  const counts = {
    acts: fillCounted(home.acts, searched.works),
    versions: fillCounted(home.versions, searched.states),
    articles: fillCounted(home.articles, searched.articles),
    language: languageName(view.language),
  };
  const builtOn = builtOnOf(searched.builtAt);
  const older = indexOlderThanToday(builtOn, today);
  return (
    <>
      <p data-searched={searched.works} data-built-on={builtOn ?? ''} data-today={older ? today : undefined}>
        {builtOn === null
          ? fillText(COPY.searchedUndated, counts)
          : older ? fillText(COPY.searchedOlder, { ...counts, date: builtOn, today }) : fillText(COPY.searched, { ...counts, date: builtOn })}
      </p>
      {searched.withheldByRights > 0 ? <p data-withheld={searched.withheldByRights}>{fillCounted(COPY.withheld, searched.withheldByRights)}</p> : null}
      {searched.severalOnDate > 0 ? <p data-several-on-date={searched.severalOnDate}>{fillCounted(COPY.severalOnDate, searched.severalOnDate, { date: view.date })}</p> : null}
    </>
  );
}

/**
 * The plain no-hit card (the plan's item 4.5; R59, R90): which searches ran, each said as what it found; that only exact
 * matching ran, and what that means, when the answer counted no relaxed lane; the publishers' own search pages, links only;
 * and the absence note.
 */
function NoHitCard({ view, today = null }) {
  const europe = view.publisher === 'eu-eurlex';
  const resolution = view.workResolution.outcome;
  const reference = view.reference === 'not_run_identifier_given'
    ? COPY.runReferenceNotRun
    : view.referenceReason === 'title_not_held' ? COPY.titleNotHeld : COPY.runReference;
  const titles = {
    not_run_identifier_given: COPY.runTitlesNotRun,
    no_titles_held: COPY.noTitlesHeld,
    no_title_match: COPY.runTitles,
    one_work: countedEntry(COPY.runTitlesMatched, 1),
    several_candidates: countedEntry(COPY.runTitlesMatched, view.workResolution.candidates?.length ?? 2),
  }[resolution];
  // Titles are matched with capitals and accents ignored, as the text is: said where the ladder ran over titles held.
  const titlesFolded = ['no_title_match', 'one_work', 'several_candidates'].includes(resolution);
  return (
    <section className="no-hit-card" data-no-hit-card="">
      <p data-no-hit="">
        {/* With no work named, a search is of Luxembourg law alone, and says so. */}
        <Say template={view.identifier === null ? COPY.noHitLuxembourg : COPY.noHit} values={{ query: view.query }} /> {inEnglish(asSentence(view.matching))}
      </p>
      <h2>{COPY.searchesRun}</h2>
      <ul data-searches-run="">
        <li data-search-run="reference">{reference}</li>
        <li data-search-run="titles">{titles}{titlesFolded ? <>{' '}<span data-title-matching="">{COPY.titleMatching}</span></> : null}</li>
        {view.population.strictHits === null ? null : <li data-search-run="text">{COPY.runText}</li>}
        {view.population.relaxedHits === null ? null : <li data-search-run="every-word">{COPY.runTextWords}</li>}
      </ul>
      {view.population.relaxedHits === null ? <p data-exact-only="">{COPY.exactOnly}</p> : null}
      {europe ? <EuropeSearched view={view} /> : <Searched view={view} today={today} />}
      <div className="official-search" data-official-search="">
        <p>{COPY.officialSearch}</p>
        <ul>
          {OFFICIAL_SEARCH_PAGES.map((page) => (
            <li key={page.key}>
              <a href={page.uri} rel="external" data-publisher={page.publisher}>{COPY.officialSites[page.key]}</a>
            </li>
          ))}
        </ul>
      </div>
      {/* A search with no hit is not evidence that the law does not exist, said as the refusal card says it (journey J2). */}
      <p data-absence-note="">{liveChrome().refusalCard.absenceNote}</p>
    </section>
  );
}

/**
 * One search view, laid out: the act a title names on top, what was found and searched, the passages by act, or the no-hit
 * card. `today` is the date in Luxembourg the answer was given on (its `context.freshness.observed_at`), or null.
 */
export function SearchResultsView({ view, onNextPage, today = null }) {
  const europe = view.publisher === 'eu-eurlex';
  // With no passage, the no-hit card says that no titles are held among what ran; it is not said twice.
  const resolutionOnTop = view.hits.length === 0 && view.workResolution.outcome === 'no_titles_held' ? null : view.workResolution;
  return (
    <>
      {resolutionOnTop === null ? null : <SearchWorkResolution resolution={resolutionOnTop} />}
      {/* EU text stands below Decision 95's acknowledgement and authenticity statement, as on the reading screen. */}
      {europe && view.hits.length > 0 ? <EuropeStatements view={view} /> : null}
      <Found view={view} />
      {/* What was searched: above the passages, or inside the no-hit card, which says it among what ran. */}
      {view.hits.length === 0 ? null : europe ? <EuropeSearched view={view} /> : <Searched view={view} today={today} />}
      <SearchedOn view={view} />
      {view.referenceReason === 'title_not_held' && view.hits.length > 0 ? <p data-searched-as-words="title_not_held">{COPY.titleNotHeld}</p> : null}
      {europe ? null : <AmbiguousWorks works={view.ambiguousWorks} date={view.date} />}
      {view.hits.length === 0 ? (
        view.searchableTextHeld ? <NoHitCard view={view} today={today} /> : (
          <>
            <p data-no-hit="">
              <Say template={COPY.noText} values={{ language: view.language, languages: view.searchableLanguages.join(', ') }} />
            </p>
            <p data-absence-note="">{liveChrome().refusalCard.absenceNote}</p>
          </>
        )
      ) : (
        <>
          <p lang={ENGLISH_LANG}>{asSentence(view.pageIs)} {asSentence(view.hitUnit)}</p>
          {europe ? <EuropeWordings view={view} /> : <ActGroups view={view} />}
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
        <SearchResultsView view={outcome.view} onNextPage={onNextPage} today={todayAt(outcome.context?.freshness?.observed_at)} />
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

/** The state a fragment written as a search this form will not send is said in; nothing is asked. */
export const ADDRESS_UNREAD = 'address_unread';

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
 * already written in the field, or the address holds a search. What the field holds when the reader submits is what is
 * sent, and what is written into the address. `addressed` is false for a form that keeps nothing in its address (the 404
 * page's).
 */
export function LiveSearch({ contract, fetchImpl, variant = 'search', today = null, addressed = true }) {
  if (!SEARCH_VARIANTS.includes(variant)) throw new Error(`the search form is the home page's or the search page's, not ${JSON.stringify(variant)}`);
  const home = variant === 'home';
  const [query, setQuery] = useState('');
  const [language, setLanguage] = useState(SEARCH_LANGUAGES[0].code);
  const [identifier, setIdentifier] = useState('');
  const knownToday = useLuxembourgToday(today);
  const [date, setDate] = useState(home && today !== null ? today : '');
  const dateWritten = useRef(false);
  const [outcome, setOutcome] = useState(IDLE);
  // Whether the search in flight was the reader's own submit: only then does a reference that opens its act go there.
  const submitted = useRef(false);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createSearchSession({ contract, fetchImpl, onOutcome: setOutcome });
  }

  useEffect(() => () => session.current.cancel(), []);

  // A reference the platform holds opens its act: the screen goes to the address, under this page's language, once the
  // answer says what was read (the line stays in the page this one leaves behind), when the reader asked it here.
  useEffect(() => {
    if (outcome.state === 'opens' && submitted.current) window.location.assign(outcome.href);
  }, [outcome]);

  // Today, from this browser's clock, when no server said it: after hydration, so the page the server rendered is the
  // page hydrated, and never the build's time.
  useEffect(() => {
    if (home && knownToday !== null && !dateWritten.current) setDate((current) => (current === '' ? knownToday : current));
  }, [knownToday]);

  // The search the address holds (R109), once hydrated, and whenever the reader writes another into it: the form takes it
  // and asks it. A position after "#" is no search, and changes nothing.
  useEffect(() => {
    if (!addressed) return undefined;
    const fromAddress = () => {
      const read = readSearchFragment(window.location.hash, { work: !home });
      if (read.state === 'none') return;
      session.current.cancel();
      submitted.current = false;
      if (read.state === 'refused') {
        setOutcome({ state: ADDRESS_UNREAD, sentence: COPY.addressUnread });
        return;
      }
      const { request } = read;
      dateWritten.current = true;
      setQuery(request.query);
      setLanguage(request.language);
      setIdentifier(request.identifier);
      setDate(request.date);
      session.current.ask(request);
    };
    fromAddress();
    window.addEventListener('hashchange', fromAddress);
    return () => window.removeEventListener('hashchange', fromAddress);
  }, []);

  // Today, said beside the date field: under it on the search page; on the home page, whose date and language share a
  // row, under the two.
  const todayHint = <TodayHint id={TODAY_HINT} today={knownToday} />;

  return (
    <div className={home ? 'search-home' : undefined}>
      <form
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          const request = { query, language, identifier: home ? '' : identifier, date };
          submitted.current = true;
          // The search, into the page's own address after "#", when the form would send it; the words go to the API in the
          // request's body alone.
          if (addressed) {
            let fragment = null;
            try {
              fragment = searchFragment(request, { work: !home });
            } catch {
              fragment = null;
            }
            if (fragment !== null) replacePageAddress(window, { hash: fragment });
          }
          session.current.ask(request);
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
