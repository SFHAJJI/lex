// The Changes page (the owner's brief of 2026-11-15, V4Changes; the validated mockup's left column): what changed in
// Luxembourg law in a period, one row per new version of a text, newest first.
//
// The server renders the controls and the loading line; the browser, once it knows today in Luxembourg, asks the last 30
// days and fills the rows. What an answer means is decided elsewhere: `scripts/live-radar.mjs` builds the requests, decides
// the period and its windows, and reads each text's own title (`ownTitleOf`, the brief's trap 2); `scripts/radar-answer.mjs`
// reads the radar; `RefusalCard` lays out a refusal. This file lays the page out and marks the state on the answer
// (`data-answer-state`).
//
// A row: the version's date (dd/mm/yyyy), the text's own title in its language, the articles added, changed and identical
// in words, and "See what changed", the text page in compare mode on that version and the one before (`textHref`). No hash,
// identifier, address or ISO date is shown (the brief, section 9); the platform's caveat stays, as one quiet line at the end.

import { useEffect, useMemo, useRef, useState } from 'react';

import { RefusalCard } from './RefusalCard.jsx';
import { FindIcon } from './Document.jsx';
import {
  PRESETS,
  RADAR_PLATFORM_CAVEAT,
  ROWS_PER_PAGE,
  WINDOW_DAYS,
  anchorOf,
  countsSentence,
  createChangesSession,
  languageTagOf,
  numericDate,
  periodOf,
  shownRows,
  textIdentifierOf,
  typedDate,
} from '../scripts/live-radar.mjs';
import { ENGLISH_LANG, fillText, liveChrome } from '../scripts/live-chrome.mjs';
import { actNameOf } from '../scripts/act-names.mjs';
import { textHref } from '../scripts/text-address.mjs';
import { datedByEntryIntoForce } from '../scripts/date-role.mjs';
import { FailureFrame, LiveAnswer, StatusSentence, refusalCardCopyFor } from './LiveAnswer.jsx';
import { useLuxembourgToday } from './Today.jsx';

/** This page's sentences, from the interface copy table. */
const COPY = liveChrome().radar;

/** The ids the page's labels and descriptions point at (one Changes page per document). */
const IDS = Object.freeze({ from: 'lx-changes-from', to: 'lx-changes-to', filter: 'lx-changes-filter', anchor: 'lx-changes-anchor', bad: 'lx-changes-bad' });

const LOADING = Object.freeze({ state: 'loading', sentence: liveChrome().common.loading });

/** The most titles the filter asks for beyond the rows shown, so a filter never asks a whole year's dossiers. */
const FILTER_TITLES = 24;

/** A platform phrase as a sentence: its first letter capitalised, a full stop after it unless it has one. */
function asSentence(text) {
  const capitalised = `${text.charAt(0).toUpperCase()}${text.slice(1)}`;
  return /[.!?]$/.test(capitalised) ? capitalised : `${capitalised}.`;
}

/**
 * A row's text named as the page shows it, and as the filter reads it: its own title when it has been read, else its act's
 * name in words (`actNameOf`), else the sentence that its title is not held. Never its identifier.
 */
export function shownTitleOf(row, titles) {
  const own = titles?.[row.workKey];
  if (own !== undefined && own !== null) return own.title;
  return actNameOf(row.workKey) ?? COPY.untitled;
}

/** A row's title: the text's own, in its language; a grey line while it is asked; else the act's name in the page's words. */
function RowTitle({ row, titles }) {
  const own = titles?.[row.workKey];
  if (titles !== undefined && own === undefined) {
    return (
      <span className="lx-change-title lx-change-title-loading" data-title="loading">
        <span className="lx-visually-hidden">{COPY.titleLoading}</span>
      </span>
    );
  }
  if (own !== undefined && own !== null) {
    return <span className="lx-change-title" lang={languageTagOf(own.language)} data-title="own">{own.title}</span>;
  }
  return <span className="lx-change-title" data-title="named">{actNameOf(row.workKey) ?? COPY.untitled}</span>;
}

/** One new version of a text. */
function ChangeRow({ row, titles }) {
  const identifier = textIdentifierOf(row);
  const compared = row.reason === null && row.baseline !== null;
  const href = compared
    ? textHref({ identifier, from: row.baseline.applicabilityDate, to: row.state.applicabilityDate })
    : textHref({ identifier, date: row.state.applicabilityDate });
  return (
    <li className="lx-change" data-reason={row.reason ?? (row.wordingChanged ? 'changed' : 'unchanged')}>
      <span className="lx-change-date">{numericDate(row.state.applicabilityDate)}</span>
      <span className="lx-change-body">
        <RowTitle row={row} titles={titles} />
        <span className="lx-change-counts">{row.reason === null ? countsSentence(row.counts) : COPY.reason[row.reason]}</span>
        {datedByEntryIntoForce(row.state)
          ? <span className="lx-change-note">{fillText(COPY.entryIntoForce, { date: numericDate(row.state.applicabilityDate) })}</span>
          : null}
      </span>
      {href === null ? null : (
        <a className="lx-change-link" href={href} data-compare-link={compared ? '' : undefined}>
          {compared ? COPY.seeWhatChanged : COPY.readVersion}
        </a>
      )}
    </li>
  );
}

/** The sentence a period with no row shown says. */
function NoRow({ view, period, filter, hideUnchanged }) {
  const { population } = view;
  let sentence;
  if (!population.windowOverlapsWhatIsHeld && population.firstDateHeld !== null && population.lastDateHeld !== null) {
    sentence = fillText(COPY.windowMisses, { first: numericDate(population.firstDateHeld), last: numericDate(population.lastDateHeld) });
  } else if (filter.trim() !== '') {
    sentence = COPY.noMatch;
  } else {
    sentence = fillText(hideUnchanged ? COPY.noChange : COPY.noVersion, { from: numericDate(period.from), to: numericDate(period.to) });
  }
  return <p className="lx-changes-empty" data-no-row="">{sentence}</p>;
}

/** "Show earlier changes", or what stands in its place: the earlier window loading, its failure, or that all is shown. */
function Earlier({ earlier, more, any }) {
  if (earlier.loading) return <p className="lx-changes-more" role="status">{COPY.loadingEarlier}</p>;
  if (earlier.failed) {
    return (
      <p className="lx-changes-more" role="status">
        {COPY.earlierFailed}{' '}
        <button type="button" className="lx-changes-link-button" onClick={earlier.onRetry}>{COPY.tryAgain}</button>
      </p>
    );
  }
  if (more || earlier.windowsLeft > 0) {
    return (
      <p className="lx-changes-more">
        <button type="button" className="lx-changes-link-button" data-show-earlier="" onClick={earlier.onMore}>{COPY.showEarlier}</button>
      </p>
    );
  }
  return any ? <p className="lx-changes-more lx-changes-all">{earlier.cut ? COPY.cut : COPY.allShown}</p> : null;
}

/**
 * One period's rows, laid out: newest first, those the switch and the filter keep, `shown` of them, then "Show earlier
 * changes" (`earlier`, the page's; none for a render of one answer) and the platform's caveat. `titles` holds the titles
 * read so far by text; undefined, none is asked and each row is named by its act's name.
 */
export function RadarView({ view, titles, hideUnchanged = false, filter = '', shown = Infinity, earlier = null }) {
  const rows = shownRows(view.rows, { hideUnchanged, filter, titleOf: (row) => shownTitleOf(row, titles) });
  const page = rows.slice(0, shown);
  const period = view.period ?? view.window;
  const reached = earlier?.loadedFrom ? { from: earlier.loadedFrom, to: period.to } : period;
  return (
    <>
      {rows.length === 0 ? <NoRow view={view} period={reached} filter={filter} hideUnchanged={hideUnchanged} /> : (
        <ol className="lx-changes-list" data-rows={page.length}>
          {page.map((row) => <ChangeRow key={`${row.state.stateSha256} ${row.state.language}`} row={row} titles={titles} />)}
        </ol>
      )}
      {earlier === null ? null : <Earlier earlier={earlier} more={rows.length > page.length} any={rows.length > 0} />}
      {view.caveat === RADAR_PLATFORM_CAVEAT
        ? <p data-caveat="">{COPY.caveat}</p>
        : <p data-caveat="" lang={ENGLISH_LANG}>{asSentence(view.caveat)}</p>}
    </>
  );
}

/** One view state, laid out: the rows, the refusal card, or the sentence a state carries. */
export function RadarAnswerView({ outcome, onRetry, titles, hideUnchanged = false, filter = '', shown = Infinity, earlier = null }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        <RadarView view={outcome.view} titles={titles} hideUnchanged={hideUnchanged} filter={filter} shown={shown} earlier={earlier} />
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

/** The switch's box, drawn: filled with a tick when on, an empty box when off. */
function SwitchBox({ on }) {
  return (
    <svg className={on ? 'lx-switch-box lx-switch-on' : 'lx-switch-box'} width="18" height="18" viewBox="0 0 18 18" aria-hidden="true" focusable="false">
      <rect className="lx-switch-frame" x="1" y="1" width="16" height="16" rx="4" />
      {on ? <path className="lx-switch-tick" d="M5 9.2 7.8 12 13 6.5" fill="none" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" /> : null}
    </svg>
  );
}

const INITIAL = Object.freeze({ outcome: LOADING, loadedFrom: null, windowsLeft: 0, cut: false, loadingEarlier: false, earlierFailed: null });

/**
 * The live page. Nothing is asked while rendering: once this browser knows today in Luxembourg the last 30 days are asked,
 * counted back from today, or from the newest date the corpus holds a version of when that is earlier (`anchorOf`, read from
 * the first answer's population; one more request then). A preset asks its period; two typed dates ask theirs on Enter.
 * The switch and the filter only choose among the rows held. Titles are asked for the rows shown, once per text.
 */
export function LiveRadar({ contract, fetchImpl }) {
  const [preset, setPreset] = useState('last30');
  const [fromText, setFromText] = useState('');
  const [toText, setToText] = useState('');
  const [badDates, setBadDates] = useState(false);
  const [filter, setFilter] = useState('');
  const [hideUnchanged, setHideUnchanged] = useState(true);
  const [state, setState] = useState(INITIAL);
  const [titles, setTitles] = useState({});
  const [shown, setShown] = useState(ROWS_PER_PAGE);
  const [anchor, setAnchor] = useState(null);
  const today = useLuxembourgToday(null);
  const session = useRef(null);
  const fromField = useRef(null);
  // The windows the page may still ask on its own to fill the rows a click asked for (an earlier window may show none).
  const wanted = useRef(0);
  const reanchored = useRef(false);
  if (session.current === null) {
    session.current = createChangesSession({
      contract,
      fetchImpl,
      onState: setState,
      onTitle: (workKey, title) => setTitles((held) => ({ ...held, [workKey]: title })),
    });
  }
  useEffect(() => () => session.current.cancel(), []);

  /** Shows a period: its two dates in the fields, its rows asked. */
  const show = (period, auto = 2) => {
    setFromText(numericDate(period.from));
    setToText(numericDate(period.to));
    setBadDates(false);
    setShown(ROWS_PER_PAGE);
    wanted.current = auto;
    session.current.open(period);
  };

  // Once today is known, the last 30 days, counted back from it.
  useEffect(() => {
    if (today === null || anchor !== null) return;
    setAnchor(today);
    show(periodOf('last30', today));
  }, [today]);

  const { outcome } = state;
  const view = outcome.state === 'success' ? outcome.view : null;

  // The first answer names the newest date held: when it is before today, the presets count back from it instead.
  useEffect(() => {
    if (view === null || reanchored.current || today === null) return;
    reanchored.current = true;
    const counted = anchorOf(today, view.population.lastDateHeld);
    if (counted === null || counted === anchor) return;
    setAnchor(counted);
    if (preset !== 'dates') show(periodOf(preset, counted));
  }, [view]);

  const rows = useMemo(
    () => (view === null ? [] : shownRows(view.rows, { hideUnchanged, filter, titleOf: (row) => shownTitleOf(row, titles) })),
    [view, hideUnchanged, filter, titles],
  );
  const page = rows.slice(0, shown);
  // The titles asked: those of the rows shown; while the filter holds words, those of the rows it reads (at most
  // `FILTER_TITLES` more), since a row is found by its own title.
  const filtering = filter.trim() !== '';
  const asked = filtering && view !== null ? shownRows(view.rows, { hideUnchanged }).slice(0, shown + FILTER_TITLES) : page;
  const askedKey = asked.map((row) => row.workKey).join(' ');

  useEffect(() => {
    if (asked.length > 0) session.current.titles(asked);
  }, [askedKey]);

  // Fewer rows shown than asked for, and earlier windows left: the next one back, a few times at most per click.
  useEffect(() => {
    if (view === null || state.loadingEarlier || state.earlierFailed !== null) return;
    if (rows.length < shown && state.windowsLeft > 0 && wanted.current > 0) {
      wanted.current -= 1;
      session.current.earlier();
    }
  }, [view, state.loadingEarlier, rows.length, shown]);

  const choose = (next) => {
    setPreset(next);
    if (next === 'dates') {
      fromField.current?.focus();
      return;
    }
    const period = periodOf(next, anchor);
    if (period !== null) show(period);
  };

  const submitDates = (event) => {
    event.preventDefault();
    const from = typedDate(fromText);
    const to = typedDate(toText);
    if (from === null || to === null || from > to) {
      setBadDates(true);
      return;
    }
    setPreset('dates');
    show({ from, to });
  };

  const more = () => {
    setShown(shown + ROWS_PER_PAGE);
    if (rows.length > shown) return;
    wanted.current = 2;
    session.current.earlier();
  };

  const lastYear = anchor === null ? null : String(Number(anchor.slice(0, 4)) - 1);
  const presetLabel = (key) => {
    if (key === 'lastYear') return fillText(COPY.presets.lastYear, { year: lastYear });
    if (key === 'last30') return fillText(COPY.presets.last30, { days: String(WINDOW_DAYS) });
    return COPY.presets[key];
  };
  const describedBy = [badDates ? IDS.bad : null, anchor === null || today === null ? null : IDS.anchor].filter((id) => id !== null).join(' ') || undefined;
  const typed = (setter) => (event) => {
    setter(event.target.value);
    setPreset('dates');
  };

  return (
    <div className="lx-changes-body">
      <form className="lx-changes-period" onSubmit={submitDates} noValidate>
        <div className="lx-segmented" role="group" aria-label={COPY.period}>
          {PRESETS.filter((key) => key !== 'lastYear' || lastYear !== null).map((key) => (
            <button key={key} type="button" className="lx-segment" aria-pressed={preset === key ? 'true' : 'false'} onClick={() => choose(key)}>
              {presetLabel(key)}
            </button>
          ))}
        </div>
        <span className="lx-date-field">
          <label>
            {COPY.from}{' '}
            <input ref={fromField} id={IDS.from} type="text" inputMode="numeric" placeholder={COPY.datePlaceholder} value={fromText} autoComplete="off" aria-describedby={describedBy} aria-invalid={badDates ? 'true' : undefined} onChange={typed(setFromText)} />
          </label>
        </span>
        <span className="lx-date-field">
          <label>
            {COPY.to}{' '}
            <input id={IDS.to} type="text" inputMode="numeric" placeholder={COPY.datePlaceholder} value={toText} autoComplete="off" aria-describedby={describedBy} aria-invalid={badDates ? 'true' : undefined} onChange={typed(setToText)} />
          </label>
        </span>
        <button className="lx-visually-hidden" type="submit">{COPY.showPeriod}</button>
      </form>
      {badDates ? <p id={IDS.bad} className="lx-changes-bad" role="alert">{COPY.badDates}</p> : null}
      <div className="lx-changes-filters">
        <div className="lx-changes-filter">
          <FindIcon />
          <label className="lx-visually-hidden" htmlFor={IDS.filter}>{COPY.filterLabel}</label>
          <input id={IDS.filter} type="search" placeholder={COPY.filterPlaceholder} value={filter} autoComplete="off" onChange={(event) => { setFilter(event.target.value); setShown(ROWS_PER_PAGE); }} />
        </div>
        <label className="lx-switch">
          <input type="checkbox" role="switch" className="lx-visually-hidden" checked={hideUnchanged} onChange={(event) => { setHideUnchanged(event.target.checked); setShown(ROWS_PER_PAGE); }} />
          <SwitchBox on={hideUnchanged} />
          {COPY.hideUnchanged}
        </label>
      </div>
      {anchor === null || today === null ? null : (
        <p id={IDS.anchor} className="lx-changes-anchor" data-counted-from={anchor}>
          {fillText(anchor < today ? COPY.countedFrom : COPY.countedFromToday, { date: numericDate(anchor) })}
        </p>
      )}
      <LiveAnswer>
        <RadarAnswerView
          outcome={outcome}
          onRetry={() => session.current.retry()}
          titles={titles}
          hideUnchanged={hideUnchanged}
          filter={filter}
          shown={shown}
          earlier={{
            loading: state.loadingEarlier,
            failed: state.earlierFailed !== null,
            windowsLeft: state.windowsLeft,
            cut: state.cut,
            loadedFrom: state.loadedFrom,
            onMore: more,
            onRetry: () => session.current.retry(),
          }}
        />
      </LiveAnswer>
    </div>
  );
}
