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
// text is named as such, never shown as an empty quotation. The form's controls carry no `name`.
//
// A permalink is a link to this site's own reading of it (`permalinks.mjs`), and the page served at a permalink asks
// that reading itself once it has loaded: the permalink itself, with its date and the text's language it names, one
// request (R64), so the API answers the version it pins or refuses (`pinned_digest_mismatch`, with the current link), never
// another version in its place; an act's readable address (R67) asks the work at today's date in its language. What the
// page says first, the cited article and what changed since, or the current link, is `PinnedReading.jsx`'s. The answer is
// still held to the address's pin: when no state (or EU wording) it shows is the one the address pins, the page says so
// above the reading rather than letting another version pass for it, and brings that notice into view; otherwise what it
// says first, or the article the address names (its last segment, or after `#` on an address moved from the shape before
// R64), or the state is brought into view, and given the focus.
//
// An EU work's reading is the wording that answers the date asked, original or consolidated: each wording
// is headed by its own date as a wording's (never an applicability date), and the acknowledgement and
// authenticity statement Decision 95 requires stand above the text, as the platform words them (in
// English, marked English on a page in another language).
//
// Between the form and the reading, the sentence that says what Lex is not (the specification of record's R50).
//
// Today, the calendar date in Luxembourg (R13; the plan's item 3.4), is said beside the form's date field, the server's when
// the page shows an answer, else this browser's clock (`Today.jsx`). At an act's readable address the page writes the date
// it read the act at into its own address (`?date=`, replacing the address, never adding to the history), so a link a
// reader copies keeps that date; a reading of another day than today says so above the reading (`PinnedLead`), and its
// pages and its whole act link to the version it shows, by its permalink.
//
// A version is read one page at a time (the plan's item 3.7; R73): the API answers a page of at most 50 articles and 96 KiB
// of text, and a version of more than one page says which pages it shows, lists its pages under "Contents" (each page by
// its first and last article, a link to that page's address, `?page=N` on the version's permalink, or on the act's readable
// address when the page stands there), and ends with the links
// to the pages before and after the ones shown. Without scripts they are plain links, and the server renders each page; with
// scripts the next page is asked in place and its articles are added below the ones shown, the focus on the first of them,
// and the link still works as a link. A version that fits one page is laid out as before.

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { ArticleNotHeld, PageNotHeld, PinnedLead, citedTitle } from './PinnedReading.jsx';
import { AsPublished, EuropeAnnexes, EuropeForceDates, FailureFrame, LiveAnswer, Permalink, Say, StatusSentence, inEnglish, refusalCardCopyFor } from './LiveAnswer.jsx';
import {
  LIVE_READING_IDLE,
  READING_LANGUAGES,
  createReadingSession,
  loadLiveReading,
  quotationLanguageTag,
  readingOutcome,
} from '../scripts/live-reading.mjs';
import { CHROME_LOCALE, ENGLISH_LANG, countedEntry, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { anchorOf, anchorText, dateOfQuery, permalinkHref, withDateQuery } from '../scripts/permalinks.mjs';
import { addedMark } from '../scripts/quotation-marks.mjs';
import { datedByEntryIntoForce } from '../scripts/date-role.mjs';
import { hasNextPage, pagesShown, readNextPage, viewOf, withoutStopped } from '../scripts/reading-pages.mjs';
import { answerHandedWith } from '../scripts/v3-client.mjs';
import { readingAddressShown, readingPinOf, readsAnotherDay, todayAt } from '../scripts/initial-answer.mjs';
import { writeLawPageLinks } from './head-links.mjs';
import { replacePageAddress } from './page-address.mjs';
import { TodayHint, useLuxembourgToday } from './Today.jsx';

/** The forms' labels and buttons, and this screen's sentences, from the interface copy table. */
const FORM = liveChrome().form;
const COPY = liveChrome().reading;
const COMMON = liveChrome().common;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_READING_IDLE });

/**
 * What a quotation carries, beside it (the launch contract's first promise): its text digest, its body
 * digest, its official source and the permalink that pins its work, article, date, language and state.
 * Shared with the export composer, which quotes the same articles (review of #805).
 */
export function QuoteEvidence({ article, linked = true }) {
  return (
    <p data-quote-evidence="">
      <Say
        template={COPY.evidence}
        values={{
          text: <code>{article.textSha256}</code>,
          body: <code>{article.bodySha256}</code>,
          source: <code>{article.officialSource}</code>,
          permalink: <Permalink value={article.permalink} linked={linked} />,
        }}
      />
    </p>
  );
}

/**
 * One article's text as the publisher wrote it, in its state's language, with its own date when it differs from the
 * state's (`applicabilityDate`, a Luxembourg state's; null for an EU wording) and its notes. Shared with the cited article
 * (`PinnedReading.jsx`), which quotes the same article first.
 */
export function ArticleText({ article, language, applicabilityDate, dateRole = null }) {
  return (
    <>
      <ArticleWording article={article} language={language} />
      <DeletedWordingNote article={article} />
      {applicabilityDate !== null && article.validityConflict ? (
        <p data-validity-conflict="">
          {/* A state dated by its entry into force is never said as applying from it (R21; the review of #987, F5). */}
          <Say
            template={dateRole === 'entry_into_force' ? COPY.validityConflictEntryIntoForce : COPY.validityConflict}
            values={{ own: article.validFrom, state: applicabilityDate }}
          />
        </p>
      ) : null}
      {(article.notes ?? []).length > 0 ? (
        <ol data-notes={article.notes.length}>
          {article.notes.map((note, index) => (
            <li key={`${index}.${note.marker}`} data-unmarked={note.unmarked ? '' : undefined}>
              <span>[{note.unmarked ? COPY.unmarkedNote : note.marker}]</span> <span lang={quotationLanguageTag(language)}>{note.text}</span>
            </li>
          ))}
        </ol>
      ) : null}
    </>
  );
}

/**
 * One article's wording in its state's language. An article with segments (content /3) shows quoted wording between
 * quotation marks, unless the publisher writes them in the wording itself, and the wording the publisher marks deleted
 * struck through, in a `del` that says so to a screen reader (its labels spaced apart from the words around them); its
 * text segments are exactly its text. No mark is added where the wording already holds one (the review of #981, F1), and a
 * mark the page adds stands apart from the word before or after it with a space when the wording has none there (the
 * French review of S3b, point 4); the text and its digest hold neither (`quotation-marks.mjs`). Any other article
 * is its text, as before. Shared with the export composer, which pins the same articles.
 */
export function ArticleWording({ article, language }) {
  const lang = quotationLanguageTag(language);
  if (article.segments === null || article.segments === undefined) return <blockquote lang={lang}>{article.text}</blockquote>;
  const segments = article.segments;
  return (
    <blockquote lang={lang} data-segmented="">
      {segments.map((segment, index) => {
        if (segment.kind === 'text') return <span key={index}>{segment.text}</span>;
        if (segment.kind === 'deleted_text') {
          return (
            <del key={index} data-deleted-wording="">
              <span className="visually-hidden" lang={CHROME_LOCALE}> {COPY.deletedWordingStart} </span>
              {segment.text}
              <span className="visually-hidden" lang={CHROME_LOCALE}> {COPY.deletedWordingEnd} </span>
            </del>
          );
        }
        const mark = addedMark(segments, index);
        if (mark === '') return null;
        return <span key={index} data-quotation-mark={segment.kind === 'quotation_start' ? 'open' : 'close'}>{mark}</span>;
      })}
    </blockquote>
  );
}

/** The sentence under an article that holds deleted wording: what struck-through wording is, and that it is never quoted. */
export function DeletedWordingNote({ article }) {
  return article.holdsDeletedWording ? <p data-deleted-wording-note="">{COPY.deletedWordingNote}</p> : null;
}

/** Whether a version is read in more than one page, and so lays out its pages (a version of one page is laid out as before). */
function isPaged(version) {
  return version.page.pages > 1;
}

/** The address of one page of a version's reading, on this page's language: its permalink, and `?page=N` after the first. */
export function pageHref(version, number, readable = null) {
  const base = permalinkHref(readable ?? version.permalink, CHROME_LOCALE);
  return number > 1 ? `${base}?page=${number}` : base;
}

/** What names a page's first or last article: an EU provision's heading when it has one, else the publisher's id. */
function PageEnd({ version, row, end }) {
  const heading = end === 'first' ? row.firstHeading : row.lastHeading;
  const id = end === 'first' ? row.firstPublisherId : row.lastPublisherId;
  return typeof heading === 'string' && heading.length > 0
    ? <span lang={quotationLanguageTag(version.language)}>{heading}</span>
    : <>{id}</>;
}

/** A long version's contents: each of its pages, by its first and last article, a link to the page's address. */
export function PageContents({ version, readable = null }) {
  if (!isPaged(version)) return null;
  const shown = pagesShown(version);
  return (
    <details data-contents={version.page.pages}>
      <summary>{COPY.contents}</summary>
      <ol>
        {version.pages.map((row) => (
          <li key={row.number}>
            <a href={pageHref(version, row.number, readable)} aria-current={row.number >= shown.first && row.number <= shown.last ? 'page' : undefined}>
              {row.articles === 1 ? (
                <Say template={COPY.contentsPageOne} values={{ number: row.number, first: <PageEnd version={version} row={row} end="first" /> }} />
              ) : (
                <Say
                  template={COPY.contentsPage}
                  values={{ number: row.number, first: <PageEnd version={version} row={row} end="first" />, last: <PageEnd version={version} row={row} end="last" /> }}
                />
              )}
            </a>
          </li>
        ))}
      </ol>
    </details>
  );
}

/** Which pages of a long version are shown, and their first and last article. Shared with the export composer. */
export function PagesShown({ version }) {
  if (!isPaged(version)) return null;
  const shown = pagesShown(version);
  const first = version.pages[shown.first - 1];
  const last = version.pages[shown.last - 1];
  // A page of one article names it once (the French review of 2026-10-18, point 4).
  if (shown.first === shown.last && first.articles === 1) {
    return (
      <p data-pages-shown={`${shown.first}-${shown.last}`}>
        <Say template={COPY.pageShownOne} values={{ number: shown.first, pages: version.page.pages, first: <PageEnd version={version} row={first} end="first" /> }} />
      </p>
    );
  }
  return (
    <p data-pages-shown={`${shown.first}-${shown.last}`}>
      <Say
        template={shown.first === shown.last ? COPY.pageShown : COPY.pagesShown}
        values={{
          ...(shown.first === shown.last ? { number: shown.first } : { from: shown.first, to: shown.last }),
          pages: version.page.pages,
          first: <PageEnd version={version} row={first} end="first" />,
          last: <PageEnd version={version} row={last} end="last" />,
        }}
      />
    </p>
  );
}

/** Whether a click is a plain one, which the page may take over; any other opens the link as the browser would. */
function plainClick(event) {
  return event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey;
}

/**
 * The links to the pages before and after the ones a long version shows. With scripts, the next page is asked in place
 * (`onNext`); its link stays a link, which a reader without scripts, or opening it apart, follows to that page's address.
 */
export function Pager({ version, paging = null, onNext = null, readable = null }) {
  if (!isPaged(version)) return null;
  const shown = pagesShown(version);
  const next = shown.last + 1;
  return (
    <nav data-pager="" aria-label={COPY.pager}>
      {shown.first > 1 ? (
        <a rel="prev" href={pageHref(version, shown.first - 1, readable)} data-previous-page={shown.first - 1}>{COPY.previousPage}</a>
      ) : null}
      {shown.first > 1 && hasNextPage(version) ? ' ' : null}
      {hasNextPage(version) ? (
        <a
          rel="next"
          href={pageHref(version, next, readable)}
          data-next-page={next}
          aria-busy={paging?.state === 'loading' ? 'true' : undefined}
          onClick={(event) => {
            if (onNext === null || !plainClick(event)) return;
            event.preventDefault();
            if (paging?.state !== 'loading') onNext(version);
          }}
        >
          {COPY.nextPage}
        </a>
      ) : null}
      {paging?.state === 'loading' ? <span data-page-loading="">{' '}{COMMON.loading}</span> : null}
      {paging?.state === 'failed' ? <span data-page-failed="">{' '}{COPY.nextPageFailed}</span> : null}
    </nav>
  );
}

/**
 * The places of the publisher's document right before an article that its version holds no article at (index 9's
 * `missing_articles`, S7a): said where they are, never skipped in silence.
 */
function MissingBefore({ article, state }) {
  const gap = (state.missingArticles ?? []).find((entry) => entry.beforePublisherId === article.publisherId);
  if (gap === undefined) return null;
  return (
    <p className="note" data-missing-articles={gap.count}>
      {/* The form for one names no count ("an article"), so it is given none (the French review of S7a, point 2). */}
      <Say template={countedEntry(COPY.missingBefore, gap.count)} values={gap.count === 1 ? {} : { count: gap.count }} />
    </p>
  );
}

function Article({ article, state }) {
  return (
    <li id={`${state.language}-${article.publisherId}`} data-article={article.publisherId} value={isPaged(state) ? article.position : undefined}>
      <MissingBefore article={article} state={state} />
      <h3>{article.publisherId}</h3>
      <ArticleText article={article} language={state.language} applicabilityDate={state.applicabilityDate} dateRole={state.dateRole ?? null} />
      <QuoteEvidence article={article} />
    </li>
  );
}

/**
 * What a version is and is not, said under its heading (index 9's `state_kind` and `translation_of`; S7c): an expression
 * the publisher issued in another language than French, beside the French one, of which the publisher's record does not say
 * which language version is authentic (R18); a consolidated version or a version of a code is documentation without
 * probative value, the authentic text being the act as published in the Journal officiel (the design's L5 caveat). A
 * consolidation's act is its work's `/jo`, as the platform names a base act (`base_act_iri`). Nothing on an index of schema
 * 8 or 6, which names neither.
 */
export function VersionCaveats({ state }) {
  return (
    <>
      {state.translationOf ? (
        <p className="note" data-translation-of={state.translationOf}>
          <Say template={COPY.translation} values={{ language: state.language, french: <code>{state.translationOf}</code> }} />
        </p>
      ) : null}
      {state.stateKind === 'consolidation' ? (
        <p className="note" data-caveat="consolidation">
          <Say template={COPY.consolidationCaveat} values={{ act: <code>{`${state.publisherWorkIri}/jo`}</code> }} />
        </p>
      ) : null}
      {state.stateKind === 'code' ? <p className="note" data-caveat="code">{COPY.codeCaveat}</p> : null}
    </>
  );
}

/**
 * A Luxembourg state's heading, by its date's role: a state dated by its entry into force is never said as applying from it
 * (R21, `date-role.mjs`). Shared with the export's pins and the cited article's source (S7b: both still said "the state
 * applying from" of every state, which the per-role speech test found).
 */
export function StateHeadingWords({ state, workKey }) {
  const values = { work: workKey, language: state.language, from: state.applicabilityDate };
  if (datedByEntryIntoForce(state)) {
    return state.nextApplicabilityDate === null
      ? <Say template={COPY.stateHeadingEntryIntoForce} values={values} />
      : <Say template={COPY.stateHeadingEntryIntoForceNext} values={{ ...values, next: state.nextApplicabilityDate }} />;
  }
  return state.nextApplicabilityDate === null
    ? <Say template={COPY.stateHeading} values={values} />
    : <Say template={COPY.stateHeadingNext} values={{ ...values, next: state.nextApplicabilityDate }} />;
}

function StateReading({ state, workKey, paging = null, onNext = null, readable = null }) {
  return (
    <section data-state={state.stateSha256}>
      <h2 data-date-role={state.dateRole ?? undefined}>
        <StateHeadingWords state={state} workKey={workKey} />
      </h2>
      <p>
        <Permalink value={state.permalink} />
      </p>
      <VersionCaveats state={state} />
      {/* The state's own counts, whichever of its pages are shown. */}
      <p data-counts="">
        <Say
          template={countedEntry(COPY.counts, state.page.quotedInVersion)}
          values={{
            count: state.page.quotedInVersion,
            withoutText: state.page.withoutTextInVersion,
            notAdmitted: state.articlesNotAdmitted,
            conflicts: state.page.validityConflictsInVersion,
          }}
        />
      </p>
      <PageContents version={state} readable={readable} />
      <PagesShown version={state} />
      {/* A page added in place is not read out whole: the focus moves to its first article (aria-live off). */}
      <ol className="articles" aria-live={isPaged(state) ? 'off' : undefined}>
        {state.articles.map((article) => (
          <Article key={article.articleIdentitySha256} article={article} state={state} />
        ))}
      </ol>
      {state.articlesWithoutText.length > 0 ? (
        <p data-without-text="">
          <Say template={COPY.withoutText} values={{ articles: state.articlesWithoutText.map((entry) => entry.publisherId).join(', ') }} />
        </p>
      ) : null}
      <Pager version={state} paging={paging} onNext={onNext} readable={readable} />
    </section>
  );
}

/** The digest that names a version of a reading: a Luxembourg state's, an EU wording's. */
export function versionDigest(version) {
  return version.stateSha256 ?? version.wordingSha256;
}

/** One reading view, laid out: each state's quoted text and what the bundle does not hold. */
export function ReadingView({ view, paging = {}, onNext = null, readableFor = null }) {
  return (
    <>
      <p data-rights="">
        {view.language === null ? (
          <Say template={COPY.rights} values={{ rights: view.rightsDisposition, date: view.date }} />
        ) : (
          <Say template={COPY.rightsIn} values={{ rights: view.rightsDisposition, date: view.date, language: view.language }} />
        )}
      </p>
      {(view.disclosures ?? []).map((disclosure) => (
        <p key={`${disclosure.code} ${disclosure.observedOn}`} data-rights-disclosure={disclosure.code}>
          <Say template={COPY.disclosure[disclosure.code]} values={{ date: disclosure.observedOn }} />
        </p>
      ))}
      <AsPublished asPublished={view.asPublished} />
      {view.states.map((state) => (
        <StateReading key={state.stateSha256} state={state} workKey={view.workKey} paging={paging[state.stateSha256] ?? null} onNext={onNext} readable={readableFor?.(state) ?? null} />
      ))}
      <h3>{COPY.notHeldHeading}</h3>
      <ul data-not-held={view.notHeld.length}>
        {view.notHeld.map((row) => (
          <li key={row.item}>
            <Say template={COMMON.notHeldRow} values={{ item: <strong>{row.item}</strong>, reason: inEnglish(row.reason) }} />
          </li>
        ))}
      </ul>
    </>
  );
}

function EuropeArticle({ article, wording }) {
  return (
    <li id={`${wording.language}-${article.publisherId}`} data-article={article.publisherId} value={isPaged(wording) ? article.position : undefined}>
      <h3>
        {article.publisherId}
        {article.heading.length > 0 ? (
          <>
            {' '}
            <span lang={quotationLanguageTag(wording.language)}>{article.heading}</span>
          </>
        ) : null}
      </h3>
      <blockquote lang={quotationLanguageTag(wording.language)}>{article.text}</blockquote>
      <QuoteEvidence article={article} />
    </li>
  );
}

/** One disclosure line beside an EU wording: how many other versions there are and whether their text is held here. */
function EuropeDisclosure({ template, rows, name }) {
  if (rows.length === 0) return null;
  const held = rows.filter((row) => row.textHeld).length;
  return (
    <p data-disclosure={name}>
      <Say template={countedEntry(template, rows.length)} values={{ count: rows.length, held, notHeld: rows.length - held }} />
    </p>
  );
}

/**
 * An EU wording's heading: its act, its language, and its date as the original or the consolidated wording's, never
 * an applicability date. Shared with the export composer, which reads the same wordings.
 */
export function EuropeWordingHeading({ wording, celex }) {
  return (
    <Say
      template={wording.nextDate === undefined
        ? COPY.europeWordingHeading
        : wording.kind === 'consolidated_version' ? COPY.europeConsolidatedHeading : COPY.europeOriginalHeading}
      values={{ celex, language: wording.language, date: wording.wordingDate }}
    />
  );
}

function EuropeWordingReading({ wording, celex, paging = null, onNext = null, readable = null }) {
  return (
    <section data-wording={wording.wordingSha256}>
      <h2>
        <EuropeWordingHeading wording={wording} celex={celex} />
      </h2>
      {wording.nextDate === undefined ? null : (
        <p data-wording-holds="">
          {wording.nextDate === null
            ? COPY.europeLatest
            : <Say template={COPY.europeHoldsUntil} values={{ date: wording.wordingDate, next: wording.nextDate }} />}
        </p>
      )}
      <EuropeDisclosure template={COPY.europeSameDateWorks} rows={wording.sameDateWorks} name="same-date" />
      <EuropeDisclosure template={COPY.europeUnplaced} rows={wording.unplacedVersions} name="unplaced" />
      <p>
        <Permalink value={wording.permalink} />
      </p>
      {/* The wording's own counts, whichever of its pages are shown. */}
      <p data-counts="">
        <Say
          template={countedEntry(COPY.europeCounts, wording.page.quotedInVersion)}
          values={{ count: wording.page.quotedInVersion, withoutText: wording.page.withoutTextInVersion }}
        />
      </p>
      <PageContents version={wording} readable={readable} />
      <PagesShown version={wording} />
      <ol className="articles" aria-live={isPaged(wording) ? 'off' : undefined}>
        {wording.articles.map((article) => (
          <EuropeArticle key={article.articleIdentitySha256} article={article} wording={wording} />
        ))}
      </ol>
      {wording.articlesWithoutText.length > 0 ? (
        <p data-without-text="">
          <Say template={COPY.withoutText} values={{ articles: wording.articlesWithoutText.map((entry) => entry.publisherId).join(', ') }} />
        </p>
      ) : null}
      <Pager version={wording} paging={paging} onNext={onNext} readable={readable} />
      <EuropeAnnexes rows={wording.annexesNotServed} language={wording.language} />
    </section>
  );
}

/**
 * One EU reading view, laid out: the acknowledgement and authenticity statement, each held wording's
 * quoted text, and what the bundle does not hold.
 */
export function EuropeReadingView({ view, paging = {}, onNext = null, readableFor = null }) {
  return (
    <>
      <p data-acknowledgement="" lang={ENGLISH_LANG}>{view.acknowledgement}</p>
      <p data-authenticity="" lang={ENGLISH_LANG}>{view.authenticity}</p>
      {/* The act's dates lead the reading, before its wordings, each said with its role: none of them is a wording's date. */}
      <EuropeForceDates forceDates={view.forceDates ?? null} notWording />
      {view.wordings.map((wording) => (
        <EuropeWordingReading key={wording.wordingSha256} wording={wording} celex={view.celex} paging={paging[wording.wordingSha256] ?? null} onNext={onNext} readable={readableFor?.(wording) ?? null} />
      ))}
      <h3>{COPY.notHeldHeading}</h3>
      <ul data-not-held={view.notHeld.length}>
        {view.notHeld.map((row) => (
          <li key={row.item}>
            <Say template={COMMON.notHeldRow} values={{ item: <strong>{row.item}</strong>, reason: inEnglish(row.reason) }} />
          </li>
        ))}
      </ul>
    </>
  );
}

/**
 * One view state, laid out: the reading, the refusal card, or the sentence a state carries. `paging` says, by version
 * digest, how the next page asked in place stands, and `onNext(version)` asks it (none on a page that does not page in place).
 * `readableFor(version)` names the act's readable address a version's page links go to, when the page stands at it (R67:
 * `?page=N` there, not on the permalink), else null.
 */
export function ReadingAnswerView({ outcome, onRetry, paging = {}, onNext = null, readableFor = null }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        {outcome.view.publisher === 'eu-eurlex'
          ? <EuropeReadingView view={outcome.view} paging={paging} onNext={onNext} readableFor={readableFor} />
          : <ReadingView view={outcome.view} paging={paging} onNext={onNext} readableFor={readableFor} />}
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
 * The reading form: a work identifier, a date and a language, handed to `onAsk` on submit. Shared
 * with the export composer, which asks the same reading.
 */
export function ReadingForm({ onAsk, submitLabel = FORM.submit.reading, initial = null, today = null }) {
  const [identifier, setIdentifier] = useState(initial?.identifier ?? '');
  const [date, setDate] = useState(initial?.date ?? '');
  const [language, setLanguage] = useState(initial?.language ?? '');
  // Today in Luxembourg beside the date field (R13): `today`, the server's when the page shows its answer, else this
  // browser's clock once hydrated.
  const shownToday = useLuxembourgToday(today);
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
          placeholder={FORM.datePlaceholder}
          value={date}
          autoComplete="off"
          aria-describedby={shownToday === null ? undefined : READING_TODAY_HINT}
          onChange={(event) => setDate(event.target.value)}
        />
      </label>{' '}
      <TodayHint id={READING_TODAY_HINT} today={shownToday} />
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

/** The id of the line that says today beside the reading form's date field (`TodayHint`): a page holds one such form. */
const READING_TODAY_HINT = 'reading-today';

/**
 * The live screen: a form, and the state of the last reading asked. A reading is asked when the
 * reader submits, never while rendering; the session cancels the one in flight when another is
 * asked, and unmounting cancels whatever is left.
 */
export function LiveReading({ contract, fetchImpl, initial = null }) {
  // A page the server rendered from its own answer (`initial`: the envelope and the path it answered, R67) starts with
  // that reading, read as an answer the page asked for; the browser reads the same answer from the page, so both render
  // one tree and the page asks nothing as it loads.
  const [outcome, setOutcome] = useState(() => (initial === null
    ? IDLE
    : readingOutcome(answerHandedWith('evidence_bundle', initial.envelope, { contract, objectType: 'evidence_bundle' }))));
  // The law's address this page was opened at, if any (a permalink, or an act's readable address): what it names and the
  // request that asks it (`readingPinOf`; null on a page opened at reading.html). `pinned` holds while the reading shown is
  // that request's: until the reader asks another.
  const [pin, setPin] = useState(() => (initial === null ? null : readingPinOf(initial.path, initial.envelope)));
  const [pinned, setPinned] = useState(() => initial !== null && readingPinOf(initial.path, initial.envelope) !== null);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createReadingSession({ contract, fetchImpl, onOutcome: setOutcome });
  }
  // The request the reading shown answers (the next page is the same request at that page), and the next pages asked in
  // place: `base`, the outcome they extend (a new reading drops them), `view`, the reading with them added, and `paging`,
  // by version digest, how the last one asked stands.
  const asked = useRef(initial === null ? null : readingPinOf(initial.path, initial.envelope)?.request ?? null);
  const [more, setMore] = useState({ base: null, view: null, paging: {}, focus: null });
  const nextPage = useRef(null);

  useEffect(() => () => {
    session.current.cancel();
    nextPage.current?.controller.abort();
  }, []);

  // Opened at a permalink the server did not render: ask the reading it names, once, after hydration (the server
  // rendered the idle form).
  useEffect(() => {
    if (initial !== null) return;
    const named = readingPinOf(window.location.pathname, null, undefined, window.location.search);
    if (named === null) return;
    setPin(named);
    setPinned(true);
    asked.current = named.request;
    session.current.ask(named.request);
  }, []);

  // What the address names, brought into view once the reading is shown and given the focus, as a browser does for an
  // anchor (so the keyboard and a screen reader continue from there): what the page says first (`PinnedLead`: what changed
  // since, then the cited article), else the article the address names in the pinned state (or wording), or that state
  // when no article is named. When the reading shows another version, its notice instead, and for a permalink refused as
  // no version held at its date, the notice with the current link: a reader must not land on an article without meeting
  // the notice that it is not the version the link pins. The head names the address the reading shows as the page's own,
  // and the cited article's title, as the server's render does (`readingLawPath`, `readingTitle`).
  useEffect(() => {
    if (!pinned || (outcome.state !== 'success' && outcome.state !== 'refusal')) return;
    const view = outcome.state === 'success' ? outcome.view : null;
    writeLawPageLinks(document, readingAddressShown(
      view,
      pin.address,
      outcome.state === 'refusal' ? { code: outcome.code, payload: outcome.payload } : null,
      todayAt(outcome.context?.freshness?.observed_at),
    ));
    const title = citedTitle(outcome, pin);
    if (title !== null) document.title = fillText(liveChrome().shell.title, { title });
    let target;
    if (view === null) {
      target = document.querySelector('[data-pin-not-held]') ?? document.querySelector('[data-answer-state="refusal"]');
    } else if (pin.kind === 'state' && !readingHolds(view, pin.permalink)) {
      target = document.querySelector('[data-pin-replaced]');
    } else if (document.querySelector('[data-pinned-lead]') !== null) {
      target = document.querySelector('[data-pinned-lead]');
    } else {
      const section = pin.digest === null ? null : document.querySelector(`[data-state="${pin.digest}"], [data-wording="${pin.digest}"]`);
      const anchor = anchorText(pin.anchor) ?? anchorOf(window.location.hash);
      const named = anchor === null ? undefined : [...(section ?? document).querySelectorAll('[data-article]')].find((element) => element.getAttribute('data-article') === anchor);
      // An article the state does not hold (a link made by hand, or to another version) brings the state into view.
      target = named ?? section;
    }
    if (!target) return;
    target.scrollIntoView();
    if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
    target.focus({ preventScroll: true });
  }, [pinned, pin, outcome]);

  // At an act's readable address, once the act is read: the date it was read at, into the page's own address (`?date=`;
  // R13, the plan's item 3.4), so a link a reader copies keeps it. An address that keeps a date already is left as it is.
  useEffect(() => {
    if (!pinned || pin?.kind !== 'current' || (outcome.state !== 'success' && outcome.state !== 'refusal')) return;
    if (dateOfQuery(window.location.search) !== null) return;
    replacePageAddress(window, { search: withDateQuery(window.location.search, pin.request.date) });
  }, [pinned, pin, outcome]);

  // The next page added in place, once it is shown: its first article is brought into view and given the focus.
  const focus = more.base === outcome ? more.focus : null;
  useEffect(() => {
    if (focus === null || focus === undefined) return;
    const section = document.querySelector(`[data-state="${focus.digest}"], [data-wording="${focus.digest}"]`);
    const target = [...(section?.querySelectorAll('[data-article]') ?? [])].find((element) => element.getAttribute('data-article') === focus.id);
    if (!target) return;
    target.scrollIntoView();
    if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
    target.focus({ preventScroll: true });
  }, [focus]);

  const ask = (request) => {
    setPinned(false);
    nextPage.current?.controller.abort();
    asked.current = request;
    session.current.ask(request);
  };

  // The page after the ones a version shows, asked in place: the request that asked the reading, in that version's language,
  // at that page. Its articles are added below the shown ones; a failure says so, and the link still opens the page.
  const onNext = (version) => {
    if (asked.current === null || outcome.state !== 'success') return;
    const base = outcome;
    const digest = versionDigest(version);
    const controller = new AbortController();
    // One page is asked at a time: a page still loading for another version is stopped, and its link comes back, so it can
    // be asked again (the review of #978).
    const stopped = nextPage.current;
    stopped?.controller.abort();
    nextPage.current = { controller, digest };
    const settle = (change) => setMore((current) => change(current.base === base ? current : { base, view: null, paging: {}, focus: null }));
    settle((current) => ({ ...current, base, paging: { ...withoutStopped(current.paging, stopped?.digest ?? null, digest), [digest]: { state: 'loading' } } }));
    // The articles the page shows before the new ones: the first one after them is the first of the page added.
    const before = new Set([...version.articles, ...version.articlesWithoutText].map((article) => article.publisherId));
    readNextPage({
      view: shown.view,
      version,
      request: asked.current,
      ask: async (request) => viewOf(await loadLiveReading({ contract, fetchImpl, signal: controller.signal, request })),
    })
      .then(({ view, version: now }) => {
        if (controller.signal.aborted) return;
        const first = [...now.articles, ...now.articlesWithoutText].sort((left, right) => left.position - right.position)
          .find((article) => !before.has(article.publisherId))?.publisherId;
        settle((current) => ({ ...current, view, paging: { ...current.paging, [digest]: { state: 'added' } }, focus: first === undefined ? null : { digest, id: first } }));
      })
      .catch(() => {
        if (!controller.signal.aborted) settle((current) => ({ ...current, paging: { ...current.paging, [digest]: { state: 'failed' } } }));
      });
  };
  // The failure panel's retry (R60): the last reading asked, or on a page the server rendered from a failure, the reading
  // its permalink names, as the page would have asked it. The pin stays as it was.
  const retry = () => {
    if (!session.current.retry() && pin !== null) session.current.ask(pin.request);
  };
  const replaced = pinned && pin.kind === 'state' && outcome.state === 'success' && !readingHolds(outcome.view, pin.permalink);
  const shown = more.base === outcome && more.view !== null ? { ...outcome, view: more.view } : outcome;
  // At an act's readable address, a page's links go to that address's pages (`?page=N`), not to the permalink's; at one that
  // keeps another date than today, to the permalink's, the version it shows (today's address shows another).
  const readableFor = pinned && pin.kind === 'current' && !readsAnotherDay(pin, outcome) ? (version) => (version.language === pin.language ? pin.act : null) : null;
  return (
    <div>
      <ReadingForm key={pin === null ? 'blank' : pin.address} initial={pin?.form ?? null} onAsk={ask} today={todayAt(outcome.context?.freshness?.observed_at)} />
      {/* What Lex is not, above the text it shows (the specification of record's R50: Reading says it). */}
      <p className="not-advice" data-not-advice="">{liveChrome().site.notAdvice}</p>
      <LiveAnswer>
        {pinned && !replaced ? <PinnedLead outcome={outcome} pin={pin} /> : null}
        {replaced ? (
          <p data-pin-replaced="">
            <Say
              template={outcome.view.publisher === 'eu-eurlex' ? COPY.europePinReplaced : COPY.pinReplaced}
              values={{ pinned: <code>{pin.permalink}</code> }}
            />
          </p>
        ) : null}
        <ReadingAnswerView outcome={shown} onRetry={retry} paging={more.base === outcome ? more.paging : {}} onNext={onNext} readableFor={readableFor} />
        {/* After the card that says the article is not held, the whole act (the French review of 2026-10-18, point 2). */}
        {pinned ? <ArticleNotHeld outcome={outcome} pin={pin} /> : null}
        {/* After the sentence that says the version has no such page, its first page and its last (the review of #978, F2). */}
        {pinned ? <PageNotHeld outcome={outcome} pin={pin} /> : null}
      </LiveAnswer>
    </div>
  );
}

/** Whether a reading view shows the state (or EU wording) a permalink pins. */
export function readingHolds(view, permalink) {
  if (view.publisher === 'eu-eurlex') return view.wordings.some((wording) => wording.permalink === permalink);
  return view.states.some((state) => state.permalink === permalink);
}
