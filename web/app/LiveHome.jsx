// The home page's screen (the specification of record's R47, R52, R11, R13 and R50).
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

import { LiveSearch } from './LiveSearch.jsx';
import { UNMOUNTED_SENTENCE, startHomeCoverage } from '../scripts/live-home.mjs';
import { CHROME_LOCALE, fillCounted, fillText, liveChrome, livePath, refusalSentence } from '../scripts/live-chrome.mjs';
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
    return (
      <p className="coverage-line coverage-short" data-coverage-line="counted" data-built-on={line.builtOn}>
        {fillText(COPY.coverageShort, { acts: counts.acts, articles: counts.articles, date: line.builtOn })}
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
        <a href={livePath(referencePage('decisions').file)}>{liveChrome().decisions.title}</a>
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

/**
 * The home page's screen. `contract` and `fetchImpl` must be stable for the screen's life; `examples`, the record of example
 * searches, is the one this bundle was compiled with unless a test hands another.
 */
export function LiveHome({ contract, fetchImpl, facts = null, examples = HOME_EXAMPLES }) {
  const [line, setLine] = useState(facts?.coverage ?? null);
  const [assistant, setAssistant] = useState(facts?.assistant === true);
  // Today in Luxembourg: the server's, when it rendered the page from its answer, else this browser's clock once hydrated.
  const today = useLuxembourgToday(facts?.today ?? null);
  // The page as built asks for its coverage line once loaded; a page the server rendered carries it.
  useEffect(() => (facts === null ? startHomeCoverage({ contract, fetchImpl, onLine: setLine, onAssistant: setAssistant }) : undefined), []);
  // With the assistant on (the owner's order of 2026-11-14, evening): the question box, its date and its button on one row,
  // three example questions, and the coverage line, and nothing else. With it off, the search box is the home page's only box
  // (R47), with what Lex is, its example searches and the pages a reader starts from.
  if (assistant) {
    return (
      <>
        <HomeAsk />
        <CoverageLine line={line} today={today} short />
      </>
    );
  }
  return (
    <>
      <section className="home-search" data-home-search="">
        <LiveSearch contract={contract} fetchImpl={fetchImpl} variant="home" today={facts?.today ?? null} />
      </section>
      <HomeTrust assistant={assistant} />
      <section className="home-about" data-home-about="">
        <h2>{COPY.aboutHeading}</h2>
        <p>{COPY.intro}</p>
      </section>
      <CoverageLine line={line} today={today} />
      <HomeExamples line={line} record={examples} />
      <HomeLinks assistant={assistant} />
    </>
  );
}
