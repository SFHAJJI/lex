// The home page's screen (the specification of record's R47, R52, R11, R13 and R50).
//
// In this order, so that on a phone the box comes first and the sentence under it stays in view (R47):
// - the one box (`LiveSearch`, its home form): the placeholder R52 words, a date field that starts at today, the calendar
//   date in Luxembourg (R13), and the language; under it, the sentence that says what Lex is not (R50), and the answer;
// - what Lex is and is not: the site's introduction, under its own heading;
// - the coverage line (R11): every count the coverage answer's, said "as of the index built on" its build's date
//   (`live-home.mjs`), and today beside it when the index is older (R13: "When the index is older, both dates are shown");
// - links to the site's served pages that a reader starts from: Changes, and Trust and Coverage. Developers and Get help
//   have no live page yet, so nothing links to them (R52: nothing unserved is shown).
//
// No example searches: the coverage answer and the browse answer name no title and no text (browse names a work by its
// identifiers only), so no example could be shown to land on text this server holds without a literal the mount may not
// hold (R11). They come when an answer serves them (STATUS-WEB.md). No assistant, chip or toggle either (R52).
//
// `facts`, when the server rendered the page from its coverage answer, are today and the coverage line it said; the
// browser hydrates the same tree from them, so nothing is asked as the page loads. With none (the page as built), the
// page asks `coverage` once loaded, and the search form takes today from this browser's clock.

import { useEffect, useState } from 'react';

import { LiveSearch } from './LiveSearch.jsx';
import { UNMOUNTED_SENTENCE, startHomeCoverage } from '../scripts/live-home.mjs';
import { fillCounted, fillText, liveChrome, livePath, refusalSentence } from '../scripts/live-chrome.mjs';
import { indexOlderThanToday } from '../scripts/live-search.mjs';
import { useLuxembourgToday } from './Today.jsx';

const COPY = liveChrome().home;

/**
 * The coverage line: the answer's counts and its index's build date, with today beside it when the index is older (`today`,
 * the date in Luxembourg, or null), or the coverage page's sentence for no mount.
 */
export function CoverageLine({ line, today = null }) {
  if (line === null || line.state === 'unavailable') return null;
  if (line.state === 'unmounted') return <p className="coverage-line" data-coverage-line="unmounted">{refusalSentence(UNMOUNTED_SENTENCE)}</p>;
  const counts = {
    acts: fillCounted(COPY.acts, line.works),
    versions: fillCounted(COPY.versions, line.states),
    articles: fillCounted(COPY.articles, line.articles),
  };
  const older = indexOlderThanToday(line.builtOn, today);
  return (
    <p className="coverage-line" data-coverage-line="counted" data-built-on={line.builtOn ?? ''} data-today={older ? today : undefined}>
      {line.builtOn === null
        ? fillText(COPY.coverageUndated, counts)
        : older ? fillText(COPY.coverageOlder, { ...counts, date: line.builtOn, today }) : fillText(COPY.coverage, { ...counts, date: line.builtOn })}
    </p>
  );
}

/** The site's served pages a reader starts from, by the names their own pages give them. */
export function HomeLinks() {
  return (
    <nav aria-label={COPY.links} className="home-links" data-home-links="">
      <ul>
        <li><a href={livePath('radar.html')}>{liveChrome().site.radar}</a></li>
        <li><a href={livePath('coverage.html')}>{liveChrome().coverage.title}</a></li>
      </ul>
    </nav>
  );
}

/** The home page's screen. `contract` and `fetchImpl` must be stable for the screen's life. */
export function LiveHome({ contract, fetchImpl, facts = null }) {
  const [line, setLine] = useState(facts?.coverage ?? null);
  // Today in Luxembourg: the server's, when it rendered the page from its answer, else this browser's clock once hydrated.
  const today = useLuxembourgToday(facts?.today ?? null);
  // The page as built asks for its coverage line once loaded; a page the server rendered carries it.
  useEffect(() => (facts === null ? startHomeCoverage({ contract, fetchImpl, onLine: setLine }) : undefined), []);
  return (
    <>
      <LiveSearch contract={contract} fetchImpl={fetchImpl} variant="home" today={facts?.today ?? null} />
      <section className="home-about" data-home-about="">
        <h2>{COPY.aboutHeading}</h2>
        <p>{COPY.intro}</p>
      </section>
      <CoverageLine line={line} today={today} />
      <HomeLinks />
    </>
  );
}
