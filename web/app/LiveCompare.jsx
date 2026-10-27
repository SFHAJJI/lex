// The compare screen, asked of the live API: two states of one work, article by article.
//
// The server renders the form and the idle state, or, at a comparison's address (`/en/compare.html?work=/lu-legilux/…&from=…
// &to=…`, the specification of record's R57 and R67; the audit of 2026-10-27, gap 2), the comparison itself from its own
// answer, readable without JavaScript; the browser then asks only when the reader submits. The form's controls carry no
// name (R109), so a submit the browser performs itself sends nothing; once a comparison is asked, the screen writes its
// address into the page's when its work is this site's address of an act (`compareQueryOf`: a law's address and two dates,
// never a word; a driver decision, STATUS-WEB.md), so it can be shared.
//
// A changed article shows its wording inline (R57): one column, in reading order, the words deleted struck through and the
// words inserted underlined, each also said in words hidden from sight, so a screen reader reads it linearly and nothing is
// said by colour alone; an added article is its text inserted, a removed one its text deleted. A jump list leads to each
// change, and each change to the previous and the next. The wording is the text each side serves (`diff`'s `text_rule`),
// compared word by word (`scripts/word-diff.mjs`); the status is still the digests'. It is served in pages (the review of
// #1003, F4: at most 50 articles and 96 KiB a page, as a reading's): a comparison of more than one page says which page it
// shows, by its first and last article, links to the pages before and after it (at the comparison's address, `page=N`, which
// the server renders; a comparison with no address asks the page in place), and says of each change on another page that its
// wording is there. What an answer means is decided
// elsewhere: `scripts/live-compare.mjs` builds the request
// and maps the answer to a view state, `scripts/compare-answer.mjs` reads and recomputes the
// comparison, and `RefusalCard` lays out a refusal. This file lays the view out and marks the state
// on the answer (`data-answer-state`).
//
// The comparison is by the publisher's article id and wording digest, and says nothing about legal
// effect; the page says the platform's own note beside it, except when both dates select one state, which it
// says in its own language and the publisher's clock (`compare.sameVersion`). An act held only as published (the review
// of #964) shows the as-published block (`AsPublished`), and both dates selecting its one state is said as what it is,
// the one text held (`compare.sameTextHeld`), with the consolidated versions and the end-of-force date its publisher's
// record dates between the two dates, each named (`compare.betweenNamed`, `compare.betweenEndOfForce`), and nothing
// said of whether the text changed. It is linear, not a colour-coded diff:
// each status is a word, and the changed, added and removed articles are listed before the unchanged
// ones, which are one disclosure away. Every permalink is a link to its page (`Permalink`).

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { COMPARE_LANGUAGES, LIVE_COMPARE_IDLE, compareOutcome, createCompareSession } from '../scripts/live-compare.mjs';
import { CHROME_LOCALE, ENGLISH_LANG, countedEntry, liveChrome } from '../scripts/live-chrome.mjs';
import { AsPublished, FailureFrame, LiveAnswer, Permalink, Say, StatusSentence, inEnglish, refusalCardCopyFor } from './LiveAnswer.jsx';
import { todayAt } from '../scripts/initial-answer.mjs';
import { TodayHint, useLuxembourgToday } from './Today.jsx';
import { quotationLanguageTag } from '../scripts/live-reading.mjs';
import { compareOfQuery, compareQueryOf } from '../scripts/permalinks.mjs';
import { replacePageAddress } from './page-address.mjs';
import { answerHandedWith } from '../scripts/v3-client.mjs';

/** The forms' labels and buttons, and this screen's sentences, from the interface copy table. */
const FORM = liveChrome().form;
const COPY = liveChrome().compare;
const COMMON_LIST_SEPARATOR = liveChrome().common.listSeparator;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_COMPARE_IDLE });

/** A platform phrase as a sentence: its first letter capitalised, a full stop after it unless it has one. */
function asSentence(text) {
  const capitalised = `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
  return /[.!?]$/.test(capitalised) ? capitalised : `${capitalised}.`;
}

function Side({ label, side }) {
  // A side dated by its entry into force is the act as published, never "the state applying from" that date (R21, S7b).
  const entryIntoForce = side.dateRole === 'entry_into_force';
  const template = side.nextApplicabilityDate === null
    ? (entryIntoForce ? COPY.sideEntryIntoForce : COPY.side)
    : (entryIntoForce ? COPY.sideEntryIntoForceNext : COPY.sideNext);
  return (
    <p data-side={label} data-date-role={side.dateRole ?? undefined}>
      {/* The next state's date is said as that state's start, never as this one's end: "until" would
          leave open whether the boundary day is this state's (review of #783). */}
      <Say
        template={template}
        values={{
          side: label,
          from: side.applicabilityDate,
          ...(side.nextApplicabilityDate === null ? {} : { next: side.nextApplicabilityDate }),
          articles: side.articleCount,
          conflicts: side.validityConflictCount,
          permalink: <Permalink value={side.permalink} />,
        }}
      />
    </p>
  );
}

/**
 * A changed article's wording, inline (R57): one column, in reading order, the words deleted struck through (`del`) and the
 * words inserted underlined (`ins`), each said in words for a screen reader and for any reader without the page's styles
 * (`COPY.deletedStart` … `COPY.insertedEnd`, hidden from sight), so nothing is said by colour or decoration alone. The
 * quotation is marked in the text's language, the words around each change in the page's.
 */
function Wording({ wording, language }) {
  if (wording === null) return null;
  if (wording.notServed !== undefined) {
    return <p data-wording-not-served={wording.notServed.join(' ')}>{COPY.wordingNotServed[wording.notServed.length === 2 ? 'both' : wording.notServed[0]]}</p>;
  }
  const said = (text) => <span className="visually-hidden" lang={CHROME_LOCALE}>{text}</span>;
  return (
    <>
      <p className="wording-diff" lang={quotationLanguageTag(language)} data-wording={wording.complete ? 'words' : 'whole'}>
        {wording.parts.map((part, index) => {
          if (part.kind === 'same') return <span key={index}>{part.text}</span>;
          if (part.kind === 'del') return <del key={index}>{said(`${COPY.deletedStart} `)}{part.text}{said(COPY.deletedEnd)}</del>;
          return <ins key={index}>{said(`${COPY.insertedStart} `)}{part.text}{said(COPY.insertedEnd)}</ins>;
        })}
      </p>
      {wording.complete ? null : <p data-wording-whole="">{COPY.wordingWhole}</p>}
    </>
  );
}

/** The anchor of the n-th change (from 1) of a comparison in one language, which the jump list and each change link to. */
function changeId(language, index) {
  return `change-${language}-${index + 1}`;
}

function Row({ row, language, change = null, elsewhere = false }) {
  // The whole digest, never a prefix: a prefix is not the digest a reader can check (review of #783).
  const digest = (entries) => (entries.length === 0 ? COPY.noWording : <code>{entries.map((entry) => entry.wordingSha256).join(', ')}</code>);
  return (
    <li id={change === null ? undefined : changeId(language, change.index)} data-status={row.status}>
      <Say template={COPY.row} values={{ article: <strong>{row.publisherId}</strong>, status: COPY.status[row.status], from: digest(row.from), to: digest(row.to) }} />
      <Wording wording={row.wording} language={language} />
      {elsewhere ? <p data-wording-elsewhere="">{COPY.wordingOtherPage}</p> : null}
      {change === null ? null : (
        // The previous and next change (R57), as links any reader can follow, the keyboard's included.
        <p className="change-nav" data-change-nav="">
          {change.index > 0 ? <a href={`#${changeId(language, change.index - 1)}`}>{COPY.previousChange}</a> : null}
          {change.index > 0 && change.index < change.count - 1 ? COMMON_LIST_SEPARATOR : null}
          {change.index < change.count - 1 ? <a href={`#${changeId(language, change.index + 1)}`}>{COPY.nextChange}</a> : null}
        </p>
      )}
    </li>
  );
}

/**
 * The page of a comparison's wording shown (the review of #1003, F4), by its first and last article, and the links to the pages
 * before and after it: at the comparison's address (`hrefOf`), which the server renders, or, for a comparison with no address,
 * a button that asks that page in place (`onPage`). Nothing for a comparison of one page.
 */
function WordingPages({ textPage, hrefOf, onPage }) {
  if (textPage === null || textPage.pages < 2) return null;
  const go = (page, label) => {
    const href = hrefOf(page);
    if (href !== null) return <a href={href} rel={page < textPage.page ? 'prev' : 'next'}>{label}</a>;
    return <button type="button" onClick={() => onPage?.(page)}>{label}</button>;
  };
  const before = textPage.page > 1;
  const after = textPage.page < textPage.pages;
  return (
    <div data-wording-pages={`${textPage.page}/${textPage.pages}`}>
      <p>
        <Say
          template={textPage.first === textPage.last ? COPY.wordingPageOne : COPY.wordingPage}
          values={{ page: textPage.page, pages: textPage.pages, first: textPage.first, last: textPage.last }}
        />
      </p>
      <p className="change-nav">
        {before ? go(textPage.page - 1, COPY.previousPage) : null}
        {before && after ? COMMON_LIST_SEPARATOR : null}
        {after ? go(textPage.page + 1, COPY.nextPage) : null}
      </p>
    </div>
  );
}

/** The changes of one comparison, each a link to its row (R57's jump list). */
function JumpList({ moved, language }) {
  if (moved.length < 2) return null;
  const heading = `jump-${language}`;
  return (
    <nav aria-labelledby={heading} data-jump-list={moved.length}>
      <h3 id={heading}>{COPY.jumpHeading}</h3>
      <ol>
        {moved.map((row, index) => (
          <li key={row.publisherId}>
            <a href={`#${changeId(language, index)}`}>
              <Say template={COPY.jumpItem} values={{ article: row.publisherId, status: COPY.status[row.status] }} />
            </a>
          </li>
        ))}
      </ol>
    </nav>
  );
}

/**
 * What the publisher's record dates between the two dates of a comparison of an act held only as published (the review
 * of #964): each consolidated version whose applicability date falls after the earlier date and on or before the later
 * one, in the as-published block's form, and its end-of-force date when it does. Nothing when the record dates none.
 */
function BetweenDates({ between }) {
  const { after, onOrBefore, consolidations, endOfForce } = between;
  if (consolidations.length === 0 && endOfForce.length === 0) return null;
  const common = liveChrome().common;
  return (
    <div data-between-dates={consolidations.length}>
      {consolidations.length > 0 ? (
        <>
          <p>
            <Say template={countedEntry(COPY.betweenNamed, consolidations.length)} values={{ count: consolidations.length, after, onOrBefore }} />
          </p>
          <ul>
            {consolidations.map((consolidation) => {
              const iri = <code>{consolidation.legalResourceIri}</code>;
              return (
                <li key={consolidation.legalResourceIri}>
                  {consolidation.endApplicabilityDate === null ? (
                    <Say template={common.asPublished.consolidationFrom} values={{ iri, from: consolidation.applicabilityDate }} />
                  ) : (
                    <Say template={common.asPublished.consolidationFromTo} values={{ iri, from: consolidation.applicabilityDate, to: consolidation.endApplicabilityDate }} />
                  )}
                </li>
              );
            })}
          </ul>
        </>
      ) : null}
      {endOfForce.length > 0 ? (
        <p data-end-of-force-between="">
          <Say template={COPY.betweenEndOfForce} values={{ date: endOfForce.join(common.listSeparator), after, onOrBefore }} />
        </p>
      ) : null}
    </div>
  );
}

/** The sentence one state on both dates is said in: one version's clock, or the one text held of an act held only as published. */
function sameStateTemplate(comparison) {
  // A state dated by its entry into force is said as that, never as applicable from it (R21, S7b).
  if (comparison.from.dateRole === 'entry_into_force') {
    if (comparison.onlyTextHeld === null) return COPY.sameVersionEntryIntoForce;
    return comparison.onlyTextHeld ? COPY.sameTextHeldEntryIntoForce : COPY.sameTextHeldOfSeveralEntryIntoForce;
  }
  if (comparison.onlyTextHeld === null) return COPY.sameVersion;
  return comparison.onlyTextHeld ? COPY.sameTextHeld : COPY.sameTextHeldOfSeveral;
}

function Comparison({ comparison, between, hrefOf, onPage }) {
  const moved = comparison.articles?.filter((row) => row.status !== 'unchanged') ?? [];
  // A change whose wording is on another page of the comparison (the review of #1003, F4) says so.
  const paged = comparison.textPage !== null && comparison.textPage.pages > 1;
  const kept = comparison.articles?.filter((row) => row.status === 'unchanged') ?? [];
  return (
    <section data-comparison={comparison.language}>
      <h2>{comparison.language}</h2>
      <Side label={COPY.sideFrom} side={comparison.from} />
      <Side label={COPY.sideTo} side={comparison.to} />
      {/* One state on both dates is said in the page's language and the publisher's clock, never "applied" (R21; the
          reader holds the platform's note to the same sentence): one version's, or, of an act held only as published,
          the one text held and what its publisher dates between the dates (the review of #964). Two states keep the
          platform's own note. */}
      {comparison.sameState ? (
        <>
          <p data-same-version="">
            <Say template={sameStateTemplate(comparison)} values={{ from: comparison.from.applicabilityDate }} />
          </p>
          {between === null ? null : <BetweenDates between={between} />}
        </>
      ) : (
        <p lang={ENGLISH_LANG}>{asSentence(comparison.note)}</p>
      )}
      {comparison.sameState ? null : (
        <>
          <p data-counts="">
            <Say
              template={COPY.counts}
              values={{ changed: comparison.counts.changed, added: comparison.counts.added, removed: comparison.counts.removed, unchanged: comparison.counts.unchanged }}
            />
          </p>
          <JumpList moved={moved} language={comparison.language} />
          <WordingPages textPage={comparison.textPage} hrefOf={hrefOf} onPage={onPage} />
          {moved.length > 0 ? (
            <ol data-moved={moved.length}>
              {moved.map((row, index) => (
                <Row key={row.publisherId} row={row} language={comparison.language} change={{ index, count: moved.length }} elsewhere={paged && row.wording === null} />
              ))}
            </ol>
          ) : null}
          {kept.length > 0 ? (
            <details>
              <summary><Say template={countedEntry(COPY.kept, kept.length)} values={{ count: kept.length }} /></summary>
              <ol data-kept={kept.length}>
                {kept.map((row) => <Row key={row.publisherId} row={row} language={comparison.language} />)}
              </ol>
            </details>
          ) : null}
        </>
      )}
    </section>
  );
}

/** One comparison view, laid out: each compared language, the languages not compared, the rule. */
export function CompareView({ view, onPage = null }) {
  // The address of another page of this comparison's wording, or null when its work is no address of this site of an act.
  const hrefOf = (page) => compareQueryOf({ identifier: view.identifier, dateFrom: view.dateFrom, dateTo: view.dateTo, language: view.language ?? '', page });
  return (
    <>
      <p data-compare-summary="">
        {view.language === null ? (
          <Say template={COPY.summary} values={{ work: view.workKey, from: view.dateFrom, to: view.dateTo }} />
        ) : (
          <Say template={COPY.summaryIn} values={{ work: view.workKey, from: view.dateFrom, to: view.dateTo, language: view.language }} />
        )}
      </p>
      {/* An act held only as published says so here as on its reading and its dossier (the review of #964). */}
      <AsPublished asPublished={view.asPublished} />
      {view.comparisons.map((comparison) => (
        <Comparison key={comparison.language} comparison={comparison} between={view.laterVersionsBetween} hrefOf={hrefOf} onPage={onPage} />
      ))}
      {view.languagesNotCompared.length > 0 ? (
        <ul data-not-compared={view.languagesNotCompared.length}>
          {view.languagesNotCompared.map((entry) => (
            <li key={entry.language}>
              <Say template={COPY.notCompared} values={{ language: entry.language, reason: inEnglish(entry.reason), bound: COPY.bound[entry.bound] }} />
            </li>
          ))}
        </ul>
      ) : null}
      <p lang={ENGLISH_LANG}>{asSentence(view.wordingRule)}</p>
      {view.textRule === null ? null : <p lang={ENGLISH_LANG}>{asSentence(view.textRule)}</p>}
      <p lang={ENGLISH_LANG}>{asSentence(view.validityConflictRule)}</p>
    </>
  );
}

/** One view state, laid out: the comparison, the refusal card, or the sentence a state carries. */
export function CompareAnswerView({ outcome, onRetry, onPage = null }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        <CompareView view={outcome.view} onPage={onPage} />
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

/** The id of the line that says today beside the two dates (`TodayHint`). */
const COMPARE_TODAY_HINT = 'compare-today';

/**
 * The live screen: a form, and the state of the last comparison asked. A comparison is asked when
 * the reader submits, never while rendering; the session cancels the one in flight when another is
 * asked, and unmounting cancels whatever is left. Today in Luxembourg is said beside the two dates: the day the last
 * answer was given on, else this browser's clock once hydrated (R13).
 */
/** The form's fields a comparison's address names (`compareOfQuery`), or empty ones. */
function fieldsOf(search) {
  const asked = compareOfQuery(search ?? '');
  return asked === null
    ? { identifier: '', dateFrom: '', dateTo: '', language: '', page: null }
    : { identifier: asked.identifier, dateFrom: asked.date_from, dateTo: asked.date_to, language: asked.language ?? '', page: asked.page ?? null };
}

/** The query of a path the page was rendered at (`/en/compare.html?work=…`), with its `?`, or empty. */
function searchOf(path) {
  const at = (path ?? '').indexOf('?');
  return at < 0 ? '' : path.slice(at);
}

export function LiveCompare({ contract, fetchImpl, initial = null }) {
  // A page the server rendered from its own answer (`initial`, R57 and R67: the comparison at its address, readable
  // without JavaScript) starts with that comparison and the form filled from its address; the browser reads the same
  // answer from the page, so both render one tree.
  const start = initial === null ? null : fieldsOf(searchOf(initial.path));
  const [identifier, setIdentifier] = useState(start?.identifier ?? '');
  const [dateFrom, setDateFrom] = useState(start?.dateFrom ?? '');
  const [dateTo, setDateTo] = useState(start?.dateTo ?? '');
  const [language, setLanguage] = useState(start?.language ?? '');
  const [outcome, setOutcome] = useState(() => (initial === null
    ? IDLE
    : compareOutcome(answerHandedWith('diff', initial.envelope, { contract, objectType: 'diff' }))));
  const session = useRef(null);
  if (session.current === null) {
    session.current = createCompareSession({ contract, fetchImpl, onOutcome: setOutcome });
  }
  const today = useLuxembourgToday(todayAt(outcome.context?.freshness?.observed_at));

  useEffect(() => () => session.current.cancel(), []);

  // Opened at a comparison's address the server did not render: ask it, once, after hydration.
  useEffect(() => {
    if (initial !== null) return;
    const fields = fieldsOf(window.location.search);
    if (fields.identifier === '') return;
    setIdentifier(fields.identifier);
    setDateFrom(fields.dateFrom);
    setDateTo(fields.dateTo);
    setLanguage(fields.language);
    session.current.ask(fields);
  }, []);

  return (
    <div>
      {/* The form's controls carry no name (R109): a submit the browser performs itself sends nothing. Once a comparison is
          asked, the screen writes its address into the page's when its work is this site's address of an act (a law's
          address and two dates, never a word), which the server renders (R57, R67), so it can be shared and read without
          JavaScript. */}
      <form
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          const request = { identifier, dateFrom, dateTo, language };
          const search = compareQueryOf(request);
          if (session.current.ask(request) && search !== null) replacePageAddress(window, { search });
        }}
      >
        <label>
          {FORM.workIdentifier}{' '}
          <input type="text" value={identifier} autoComplete="off" spellCheck={false} onChange={(event) => setIdentifier(event.target.value)} />
        </label>{' '}
        <label>
          {FORM.from}{' '}
          <input type="text" inputMode="numeric" placeholder={FORM.datePlaceholder} value={dateFrom} autoComplete="off" aria-describedby={today === null ? undefined : COMPARE_TODAY_HINT} onChange={(event) => setDateFrom(event.target.value)} />
        </label>{' '}
        <label>
          {FORM.to}{' '}
          <input type="text" inputMode="numeric" placeholder={FORM.datePlaceholder} value={dateTo} autoComplete="off" aria-describedby={today === null ? undefined : COMPARE_TODAY_HINT} onChange={(event) => setDateTo(event.target.value)} />
        </label>{' '}
        {/* Today in Luxembourg, beside the two dates (R13; the plan's item 3.4). */}
        <TodayHint id={COMPARE_TODAY_HINT} today={today} />
        <label>
          {FORM.language}{' '}
          <select value={language} onChange={(event) => setLanguage(event.target.value)}>
            <option value="">{FORM.anyLanguage}</option>
            {COMPARE_LANGUAGES.map((offered) => (
              <option key={offered.code} value={offered.code}>
                {offered.label}
              </option>
            ))}
          </select>
        </label>{' '}
        <button type="submit">{FORM.submit.compare}</button>
      </form>
      <LiveAnswer>
        <CompareAnswerView outcome={outcome} onRetry={() => session.current.retry()} onPage={(page) => session.current.askPage(page)} />
      </LiveAnswer>
    </div>
  );
}
