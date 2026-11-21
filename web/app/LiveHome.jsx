// The home page's screen. Since the owner's brief of 2026-11-15 the page is the V4 Home (`LiveHome`, at the end of this file:
// the mockup V1Home's left column); the components above it are the earlier home page's, kept for the callers and tests that
// still name them. What follows described that earlier page (the specification of record's R47, R52, R11, R13 and R50).
//
// In this order, so that on a phone the box comes first and the sentence under it stays in view (R47):
// - the one box (`LiveSearch`, its home form): the placeholder R52 words, a date field that starts at today, the calendar
//   date in Luxembourg (R13), and the language; under it, the sentence that says what Lex is not (R50), and the answer;
// - what Lex is and is not: the site's introduction, under its own heading;
// - the coverage line (R11): every count the coverage answer's, said "as of the index built on" its build's date
//   (`live-home.mjs`), and today beside it when the index is older (R13: "When the index is older, both dates are shown");
// - the example searches (R47; the plan's item 4.1, "example searches that land on served text"): the usual citations of
//   articles the mounted corpus holds, derived at build through the API's own answers and kept only when searching one opens
//   that article's text (`home-examples.mjs`), each a link to the search page with the search in its address (R109); shown
//   only over the index they were derived from, so never over another mount or none;
// - links to the site's served pages that a reader starts from: Changes, Trust and Coverage, Developers and Get help, the
//   specification of record's list (§3, Shell and home), each by the name its own page gives it.
//
// No example is a literal: a build handed none (the web suite's) shows none. No chip or toggle either (R52), and no assistant
// entry unless the server reports the assistant switched on (the coverage answer's capability flag, `live-home.mjs`): then,
// and only then, one more link, to the assistant's page (R47).
//
// `facts`, when the server rendered the page from its coverage answer, are today and the coverage line it said; the
// browser hydrates the same tree from them, so nothing is asked as the page loads. With none (the page as built), the
// page asks `coverage` once loaded, and the search form takes today from this browser's clock.

import { useEffect, useState } from 'react';

import {
  HOME_SCALE, UNMOUNTED_SENTENCE, numericDate, scaleX, startHomeCoverage, startHomeTimelines, startRecentChanges, textLanguageFor,
} from '../scripts/live-home.mjs';
import { HOME_TEXTS_RECORD } from '../scripts/home-texts-record.mjs';
import { searchFragment } from '../scripts/search-address.mjs';
import { textHref, textPath } from '../scripts/text-address.mjs';
import { CHROME_LOCALE, countedEntry, fillCounted, fillText, liveChrome, livePath, refusalSentence } from '../scripts/live-chrome.mjs';
import { referencePage } from '../scripts/reference-pages.mjs';
import { indexOlderThanToday } from '../scripts/live-search.mjs';
import { useLuxembourgToday } from './Today.jsx';
import { HOME_EXAMPLES, exampleHref, examplesShown } from '../scripts/home-examples.mjs';
import { citationLang } from '../scripts/citation.mjs';
import { askFragment } from '../scripts/ask-fragment.mjs';
import { readClaimDate } from '../scripts/law-page.mjs';

const COPY = liveChrome().home;

/**
 * The coverage line: the answer's counts and its index's build date, with today beside it when the index is older (`today`,
 * the date in Luxembourg, or null), or the coverage page's sentence for no mount.
 */
export function CoverageLine({ line, today = null, short = false }) {
  if (line === null || line.state === 'unavailable') return null;
  if (line.state === 'unmounted') return <p className="coverage-line" data-coverage-line="unmounted">{refusalSentence(UNMOUNTED_SENTENCE)}</p>;
  const counts = {
    acts: fillCounted(COPY.acts, line.works),
    versions: fillCounted(COPY.versions, line.states),
    articles: fillCounted(COPY.articles, line.articles),
  };
  const older = indexOlderThanToday(line.builtOn, today);
  // Beside the question box (the owner's order of 2026-11-14, evening): one grey line, the acts and articles held and the
  // index's date, every figure the coverage answer's.
  if (short && line.builtOn !== null) {
    const written = (entry, count) => fillText(countedEntry(entry, count), { count: count.toLocaleString(CHROME_LOCALE === 'fr' ? 'fr-FR' : 'en-GB') });
    return (
      <p className="coverage-line coverage-short" data-coverage-line="counted" data-built-on={line.builtOn}>
        {fillText(COPY.coverageShort, { acts: written(COPY.acts, line.works), articles: written(COPY.articles, line.articles), date: line.builtOn })}
      </p>
    );
  }
  return (
    <p className="coverage-line" data-coverage-line="counted" data-built-on={line.builtOn ?? ''} data-today={older ? today : undefined}>
      {line.builtOn === null
        ? fillText(COPY.coverageUndated, counts)
        : older ? fillText(COPY.coverageOlder, { ...counts, date: line.builtOn, today }) : fillText(COPY.coverage, { ...counts, date: line.builtOn })}
    </p>
  );
}

/**
 * The example searches for the coverage line shown (`examplesShown`): each the citation itself, in its text's language, a
 * link to the search page with that search in its address, and nothing when there is none to show.
 */
export function HomeExamples({ line, record = HOME_EXAMPLES }) {
  const examples = examplesShown(line, record);
  if (examples.length === 0) return null;
  return (
    <section className="home-examples" aria-labelledby="home-examples-heading" data-home-examples={examples.length}>
      <h2 id="home-examples-heading">{COPY.examplesHeading}</h2>
      <p>{COPY.examplesIntro}</p>
      <ul>
        {examples.map((example) => (
          <li key={example.query}>
            <a href={exampleHref(example, CHROME_LOCALE)} data-home-example={example.opens} lang={citationLang(example.language)}>{example.query}</a>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** The assistant's page with `question` asked: its address's fragment, with no-break spaces made plain ones. */
const assistantHref = (question) => `${livePath('assistant')}${askFragment(question.replace(/[  ]/gu, ' '))}`;

/**
 * The question box (2026-11-13, the owner's choice of an assistant-first home page), shown only while the server reports the
 * assistant switched on (R52): it opens the assistant's page with the question asked, and lists example questions, each asked
 * on the live site before it was listed, each a link that asks it.
 */
/**
 * An example question as it is asked: the test set's own words, whose apostrophes are straight; the page shows it in its own
 * typography (the French table's apostrophes are typographic).
 */
export const exampleAsked = (example) => example.replace(/’/gu, "'");

/** A calendar date as a question writes it, day first ("03/03/2019"), which the assistant's intake reads as a date. */
export function writtenDate(date) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/u.exec(date);
  return match === null ? '' : `${match[3]}/${match[2]}/${match[1]}`;
}

/** The question the box sends: the reader's, and the date chosen on the chip said in it, when one is (`COPY.askedOn`). */
export function questionAsked(question, date) {
  const asked = question.trim();
  if (asked.length === 0) return '';
  const written = writtenDate(date);
  return written === '' ? asked : fillText(COPY.askedOn, { question: asked, date: written });
}

export function HomeAsk({ questions = Object.values(COPY.askExamples) }) {
  const [question, setQuestion] = useState('');
  // The date (the owner's order of 2026-11-14, evening: one row, the question, the date and the button): today while it is
  // empty; else the calendar date typed, day first (`readClaimDate`), or null while it names none.
  const [dateText, setDateText] = useState('');
  const date = dateText.trim() === '' ? '' : readClaimDate(dateText);
  return (
    <section className="home-ask" aria-labelledby="home-ask-heading" data-home-ask="">
      <h2 id="home-ask-heading" className="visually-hidden">{COPY.askHeading}</h2>
      <form
        className="home-ask-form"
        onSubmit={(event) => {
          event.preventDefault();
          if (date === null) return;
          const asked = questionAsked(question, date);
          if (asked.length > 0) window.location.assign(assistantHref(asked));
        }}
      >
        <label htmlFor="home-ask-question" className="visually-hidden">{COPY.askLabel}</label>
        <input id="home-ask-question" name="question" type="text" maxLength={1000} placeholder={COPY.askPlaceholder} value={question} onChange={(event) => setQuestion(event.target.value)} />
        <label className="home-date" data-date-chip={date === '' ? 'today' : date ?? 'unread'}>
          <span>{COPY.dateChipLabel}</span>
          <input type="text" inputMode="numeric" autoComplete="off" spellCheck={false} size={10} placeholder={COPY.datePlaceholder} value={dateText} onChange={(event) => setDateText(event.target.value)} />
        </label>
        <button type="submit">{COPY.askSubmit}</button>
      </form>
      {date === null ? <p className="home-date-unread" role="status">{COPY.dateUnread}</p> : null}
      <ul className="home-ask-examples" aria-label={COPY.askTry}>
        {questions.map((example) => (
          <li key={example}>
            <a href={assistantHref(exampleAsked(example))} data-home-ask-example="">{example}</a>
          </li>
        ))}
      </ul>
    </section>
  );
}

/**
 * Why a reader can rely on Lex, in short points, with the page that says how it works; the point on the assistant's tests and
 * the page of its results only while the server reports the assistant switched on (R52: nothing unserved is shown).
 */
export function HomeTrust({ assistant = false }) {
  const points = Object.entries(COPY.trust).filter(([key]) => assistant || key !== 'tested').map(([, point]) => point);
  return (
    <section className="home-trust" aria-labelledby="home-trust-heading" data-home-trust="">
      <h2 id="home-trust-heading">{COPY.trustHeading}</h2>
      <ul>
        {points.map((point) => (
          <li key={point.title}>
            <strong>{point.title}</strong>
            <span>{point.text}</span>
          </li>
        ))}
      </ul>
      <p className="home-trust-links">
        {assistant ? <a href={livePath(referencePage('evidence').file)}>{liveChrome().evidence.title}</a> : null}
      </p>
    </section>
  );
}

/**
 * The site's served pages a reader starts from, by the names their own pages give them; the assistant's page among them only
 * while the server reports it switched on (`assistant`).
 */
export function HomeLinks({ assistant = false }) {
  return (
    <nav aria-label={COPY.links} className="home-links" data-home-links="">
      <ul>
        <li><a href={livePath('radar.html')}>{liveChrome().site.radar}</a></li>
        <li><a href={livePath('coverage.html')}>{liveChrome().coverage.title}</a></li>
        <li><a href={livePath(referencePage('developers').file)}>{liveChrome().developers.title}</a></li>
        <li><a href={livePath(referencePage('help').file)}>{liveChrome().help.title}</a></li>
        {assistant ? <li><a href={livePath('assistant')} data-home-assistant="">{COPY.assistantLink}</a></li> : null}
      </ul>
    </nav>
  );
}


// ---- V4 Home (the owner's brief of 2026-11-15, section 5; the mockup V1Home, its left column) -------------------------
//
// The headline and its sentence; the one field, which finds a text (the search page, the words after "#", R109: no model
// call); "Start with a text": six texts, each its versions drawn on one shared scale from 2006 to 2026; "Changed recently":
// the three newest changes of wording, each linked to the text page comparing that version with the one before; and one line
// of coverage with the coverage page. The examples, what the assistant does and does not, and the questions are the dock's.
//
// First paint asks nothing for the six rows (the release's record, `home-texts-record.mjs`); once loaded the page asks the
// coverage answer when the server did not render it (1), the newest changes (1 to 3) and their texts' dossiers for the titles
// (3 to 5), and only over another index than the record's, the six timelines, one at a time (6): at most 15, usually 4 or 5.

/** A count as the page's language groups it (6,001 in English, 6 001 in French). */
const grouped = (count) => count.toLocaleString(CHROME_LOCALE === 'fr' ? 'fr-FR' : 'en-GB');

/** A counted entry filled with its grouped count, or said as it is when it names none ("one version held"). */
function counted(entry, count) {
  const text = countedEntry(entry, count);
  return text.includes('{count}') ? fillText(text, { count: grouped(count) }) : text;
}

/** A text's title marked in its own language. */
const titleLang = (language) => {
  // Always said, on a page of the same language too: a title is the law's words, whose dates are the law's to write.
  return language === 'fra' ? 'fr' : language === 'eng' ? 'en' : language === 'deu' ? 'de' : undefined;
};

/** The search page's address for the words, after "#" (R109), or null for words no search would send. */
export function findHref(words) {
  try {
    return `${livePath('search.html')}${searchFragment({ query: words, language: 'fra' }, { work: false })}`;
  } catch {
    return null;
  }
}

/** The magnifier beside the field, drawn, decorative (as the header's). */
function FindIcon() {
  return (
    <svg className="lx-find-icon" width="16" height="16" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
      <circle cx="7" cy="7" r="4.6" fill="none" stroke="currentColor" strokeWidth="1.6" />
      <path d="M10.6 10.6 14 14" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
    </svg>
  );
}

/** The big field: a form with no action and a control with no name (R109); the script takes the words to the search page. */
export function HomeFind() {
  const [words, setWords] = useState('');
  return (
    <form
      className="lx-home-find"
      role="search"
      data-home-find=""
      onSubmit={(event) => {
        event.preventDefault();
        const address = findHref(words);
        if (address !== null) window.location.assign(address);
      }}
    >
      <FindIcon />
      <label className="lx-visually-hidden" htmlFor="lx-home-find-q">{COPY.findLabel}</label>
      <input id="lx-home-find-q" type="text" autoComplete="off" maxLength={512} placeholder={COPY.findPlaceholder} value={words} onChange={(event) => setWords(event.target.value)} />
      <button type="submit">{COPY.findSubmit}</button>
    </form>
  );
}

/** The years under the scale's heading: 2006, 2011, 2016, 2021 and 2026, each at its first of January. */
function ScaleYears() {
  return (
    <svg className="lx-home-years" width={HOME_SCALE.width} height="16" viewBox={`0 0 ${HOME_SCALE.width} 16`} aria-hidden="true" focusable="false">
      {HOME_SCALE.years.map((year, index) => (
        <text key={year} className="lx-home-year" x={scaleX(`${year}-01-01`)} y="12" textAnchor={index === 0 ? 'start' : 'middle'}>{year}</text>
      ))}
    </svg>
  );
}

/** A text's versions on the shared scale: one indigo tick per version, or, for a text held in one version, a hollow circle. */
export function VersionMarks({ dates }) {
  const placed = dates.map((date) => scaleX(date)).filter((x) => x !== null);
  return (
    <svg className="lx-home-marks" width={HOME_SCALE.width} height="22" viewBox={`0 0 ${HOME_SCALE.width} 22`} aria-hidden="true" focusable="false" data-marks={placed.length}>
      <line className="lx-home-axis" x1="0" y1="11" x2={HOME_SCALE.width} y2="11" />
      {dates.length === 1 && placed.length === 1
        ? <circle className="lx-home-one" cx={placed[0]} cy="11" r="4" />
        : placed.map((x, index) => <rect key={`${x} ${index}`} className="lx-home-tick" x={Math.round((x - 1.25) * 10) / 10} y="3" width="2.5" height="16" rx="1.25" />)}
    </svg>
  );
}

/** A starting text's link, which the server renders too: the text page (`textHref` builds its query by hand, for the API's renderer). */
export function startTextHref(identifier) {
  return textHref({ identifier });
}

/** One starting text: its title, where it comes from and its articles, its versions on the scale, how many. */
export function StartText({ text, row }) {
  const language = textLanguageFor(CHROME_LOCALE, Object.keys(row.versions));
  const dates = row.versions[language] ?? [];
  const articles = row.articles[language];
  const europe = text.publisher === 'eu-eurlex';
  const title = europe ? COPY.startTexts[text.identifier] ?? null : text.title;
  if (title === null || title === undefined || dates.length === 0) return null;
  const counts = Number.isInteger(articles) ? counted(COPY.articles, articles) : null;
  const origin = europe
    ? fillText(counts === null ? COPY.startEuPlain : COPY.startEu, counts === null ? { number: text.number } : { number: text.number, articles: counts })
    : counts === null ? COPY.startLuPlain : fillText(COPY.startLu, { articles: counts });
  return (
    <li>
      <a className="lx-home-text" href={startTextHref(text.identifier)} data-start-text={text.identifier}>
        <span className="lx-home-text-name">
          <span className="lx-home-text-title">{europe ? title : <span lang={titleLang(text.title_language)}>{title}</span>}</span>
          <span className="lx-home-text-origin">{origin}</span>
        </span>
        <span className="lx-home-scale-cell"><VersionMarks dates={dates} /></span>
        <span className="lx-home-count-cell">{counted(COPY.startVersions, dates.length)}</span>
      </a>
    </li>
  );
}

/** A change's counts, the first naming the articles ("1 article added, 1 changed"), the zero ones left out. */
export function changeCounts(counts) {
  const parts = ['added', 'changed', 'removed'].filter((kind) => counts[kind] > 0);
  return parts.reduce((said, kind, index) => {
    const part = counted(index === 0 ? COPY.recentFirst[kind] : COPY.recentNext[kind], counts[kind]);
    return index === 0 ? part : fillText(COPY.recentJoin, { first: said, next: part });
  }, '');
}

/** One recent change: its date, the text's own title, its counts, a link to the text page comparing it with the one before. */
function RecentChange({ row }) {
  const identifier = `/lu-legilux/${row.workKey}`;
  const href = row.from === null ? textHref({ identifier, date: row.date }) : textHref({ identifier, from: row.from, to: row.date });
  return (
    <li>
      <a className="lx-home-change" href={href} data-recent-change={row.date}>
        <span className="lx-home-change-date">{numericDate(row.date)}</span>
        <span className="lx-home-change-title"><span lang={titleLang(row.titleLanguage)}>{row.title}</span></span>
        <span className="lx-home-change-counts">{changeCounts(row.counts)}</span>
      </a>
    </li>
  );
}

/** "Changed recently": the rows once read, a line while they are asked, one sentence when they cannot be. */
export function RecentChanges({ rows }) {
  return (
    <section className="lx-home-recent" aria-labelledby="lx-home-recent-heading" data-recent={rows === undefined ? 'loading' : rows === null ? 'failed' : rows.length}>
      <div className="lx-home-recent-head">
        <h2 id="lx-home-recent-heading">{COPY.recentHeading}</h2>
        <a href={livePath('radar.html')}>{COPY.recentAll}</a>
      </div>
      {rows === undefined ? <p className="lx-home-recent-status">{COPY.recentLoading}</p> : null}
      {rows === null ? <p className="lx-home-recent-status">{COPY.recentFailed}</p> : null}
      {Array.isArray(rows) && rows.length === 0 ? <p className="lx-home-recent-status">{COPY.recentNone}</p> : null}
      {Array.isArray(rows) && rows.length > 0 ? <ul className="lx-home-changes">{rows.map((row) => <RecentChange key={`${row.workKey} ${row.date}`} row={row} />)}</ul> : null}
    </section>
  );
}

/** The coverage line: the texts and dated versions the coverage answer counts, and the coverage page. */
export function HomeHolds({ line }) {
  const counts = line?.state === 'counted'
    ? fillText(COPY.holds, { texts: counted(COPY.holdsTexts, line.works), versions: counted(COPY.holdsVersions, line.states) })
    : line?.state === 'unmounted' ? refusalSentence(UNMOUNTED_SENTENCE) : null;
  return (
    <p className="lx-home-holds" data-coverage-line={line?.state ?? 'asked'}>
      {counts === null ? null : <>{counts} </>}
      <a href={livePath('coverage.html')}>{COPY.holdsLink}</a>
    </p>
  );
}

/**
 * The home page's screen. `contract` and `fetchImpl` must be stable for the screen's life; `texts`, the starting texts'
 * record, is the release's unless a test hands another; `examples` is kept for the callers that still hand it (the dock shows
 * the examples now).
 */
export function LiveHome({ contract, fetchImpl, facts = null, examples = HOME_EXAMPLES, texts = HOME_TEXTS_RECORD }) {
  const [line, setLine] = useState(facts?.coverage ?? null);
  const [live, setLive] = useState({});
  const [recent, setRecent] = useState(undefined);
  // Today in Luxembourg: the server's, when it rendered the page from its answer, else this browser's clock once hydrated.
  const today = useLuxembourgToday(facts?.today ?? null);
  // The page as built asks for its coverage line once loaded; a page the server rendered carries it.
  useEffect(() => (facts === null ? startHomeCoverage({ contract, fetchImpl, onLine: setLine }) : undefined), []);
  useEffect(() => (today === null ? undefined : startRecentChanges({ contract, fetchImpl, today, onRows: setRecent })), [today]);
  // Over another Luxembourg index than the record's, the six timelines are asked again, after first paint, one at a time.
  const otherIndex = line?.state === 'counted' && typeof line.indexSha256 === 'string' && line.indexSha256 !== texts.indexes['lu-legilux'];
  useEffect(() => (otherIndex
    ? startHomeTimelines({ contract, fetchImpl, texts: texts.texts, today, onText: (identifier, row) => setLive((known) => ({ ...known, [identifier]: row })) })
    : undefined), [otherIndex]);
  return (
    <div className="lx-home">
      <h1 className="lx-home-headline">{COPY.headline}</h1>
      <p className="lx-home-lede">{COPY.lede}</p>
      <HomeFind />
      <section className="lx-home-start" aria-labelledby="lx-home-start-heading">
        <div className="lx-home-start-head">
          <h2 id="lx-home-start-heading">{COPY.startHeading} <span>{COPY.startNote}</span></h2>
          <span className="lx-home-scale-cell"><ScaleYears /></span>
          <span className="lx-home-count-cell" />
        </div>
        <ul className="lx-home-texts">
          {texts.texts.map((text) => <StartText key={text.identifier} text={text} row={live[text.identifier] ?? text} />)}
        </ul>
      </section>
      <RecentChanges rows={recent} />
      <HomeHolds line={line} />
    </div>
  );
}
