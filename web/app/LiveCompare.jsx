// The compare screen, asked of the live API: two states of one work, article by article.
//
// The server renders the form and the idle state, and the browser asks only when the reader
// submits. What an answer means is decided elsewhere: `scripts/live-compare.mjs` builds the request
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
// ones, which are one disclosure away. The form's controls carry no `name`, and every permalink
// is a link to its page (`Permalink`).

import { useEffect, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { COMPARE_LANGUAGES, LIVE_COMPARE_IDLE, createCompareSession } from '../scripts/live-compare.mjs';
import { ENGLISH_LANG, countedEntry, liveChrome } from '../scripts/live-chrome.mjs';
import { AsPublished, FailureFrame, LiveAnswer, Permalink, Say, StatusSentence, inEnglish, refusalCardCopyFor } from './LiveAnswer.jsx';
import { todayAt } from '../scripts/initial-answer.mjs';
import { TodayHint, useLuxembourgToday } from './Today.jsx';

/** The forms' labels and buttons, and this screen's sentences, from the interface copy table. */
const FORM = liveChrome().form;
const COPY = liveChrome().compare;

const IDLE = Object.freeze({ state: 'idle', sentence: LIVE_COMPARE_IDLE });

/** A platform phrase as a sentence: its first letter capitalised, a full stop after it unless it has one. */
function asSentence(text) {
  const capitalised = `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
  return /[.!?]$/.test(capitalised) ? capitalised : `${capitalised}.`;
}

function Side({ label, side }) {
  return (
    <p data-side={label}>
      {/* The next state's date is said as that state's start, never as this one's end: "until" would
          leave open whether the boundary day is this state's (review of #783). */}
      <Say
        template={side.nextApplicabilityDate === null ? COPY.side : COPY.sideNext}
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

function Row({ row }) {
  // The whole digest, never a prefix: a prefix is not the digest a reader can check (review of #783).
  const digest = (entries) => (entries.length === 0 ? COPY.noWording : <code>{entries.map((entry) => entry.wordingSha256).join(', ')}</code>);
  return (
    <li data-status={row.status}>
      <Say template={COPY.row} values={{ article: <strong>{row.publisherId}</strong>, status: COPY.status[row.status], from: digest(row.from), to: digest(row.to) }} />
    </li>
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
  if (comparison.onlyTextHeld === null) return COPY.sameVersion;
  return comparison.onlyTextHeld ? COPY.sameTextHeld : COPY.sameTextHeldOfSeveral;
}

function Comparison({ comparison, between }) {
  const moved = comparison.articles?.filter((row) => row.status !== 'unchanged') ?? [];
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
          {moved.length > 0 ? (
            <ol data-moved={moved.length}>
              {moved.map((row) => <Row key={row.publisherId} row={row} />)}
            </ol>
          ) : null}
          {kept.length > 0 ? (
            <details>
              <summary><Say template={countedEntry(COPY.kept, kept.length)} values={{ count: kept.length }} /></summary>
              <ol data-kept={kept.length}>
                {kept.map((row) => <Row key={row.publisherId} row={row} />)}
              </ol>
            </details>
          ) : null}
        </>
      )}
    </section>
  );
}

/** One comparison view, laid out: each compared language, the languages not compared, the rule. */
export function CompareView({ view }) {
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
      {view.comparisons.map((comparison) => <Comparison key={comparison.language} comparison={comparison} between={view.laterVersionsBetween} />)}
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
      <p lang={ENGLISH_LANG}>{asSentence(view.validityConflictRule)}</p>
    </>
  );
}

/** One view state, laid out: the comparison, the refusal card, or the sentence a state carries. */
export function CompareAnswerView({ outcome, onRetry }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        <CompareView view={outcome.view} />
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
export function LiveCompare({ contract, fetchImpl }) {
  const [identifier, setIdentifier] = useState('');
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');
  const [language, setLanguage] = useState('');
  const [outcome, setOutcome] = useState(IDLE);
  const session = useRef(null);
  if (session.current === null) {
    session.current = createCompareSession({ contract, fetchImpl, onOutcome: setOutcome });
  }
  const today = useLuxembourgToday(todayAt(outcome.context?.freshness?.observed_at));

  useEffect(() => () => session.current.cancel(), []);

  return (
    <div>
      <form
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          session.current.ask({ identifier, dateFrom, dateTo, language });
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
        <CompareAnswerView outcome={outcome} onRetry={() => session.current.retry()} />
      </LiveAnswer>
    </div>
  );
}
