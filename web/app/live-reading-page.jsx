// The reading page, live: the fourth page that asks the mounted API.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure. Built, the page is the form in its idle state and
// `client-live-reading-entry.jsx` hydrates the same tree; at a permalink the server renders it from
// its own answer (R67), and the browser hydrates the same tree from the answer the page carries.

import { Document } from './Document.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { LiveReading } from './LiveReading.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';
import { INITIAL_ANSWER_ID, jsonInScript, lawPathOf, readingAddressShown, readingPinOf, todayAt } from '../scripts/initial-answer.mjs';
import { readingOutcome } from '../scripts/live-reading.mjs';
import { answerHandedWith } from '../scripts/v3-client.mjs';
import { citedTitle } from './PinnedReading.jsx';
import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';
import { lawPageLinks, parseSitePath } from '../scripts/permalinks.mjs';
import { readingStructuredData } from '../scripts/structured-data.mjs';
import { SITE_ORIGIN } from './Document.jsx';

export const LIVE_READING_ROOT = 'live-reading-root';

/**
 * The tree the server renders and the browser hydrates: one and the same. `initial`, when the server rendered the page
 * from its own answer at a law's address (`initial-answer.mjs`), is that answer and the path it answered.
 */
export function liveReadingTree(initial = null) {
  return <LiveReading contract={LIVE_CONTRACT} initial={initial} />;
}

/**
 * The address the page at a permalink names as its own (canonical, and in each language), always one the platform mints:
 * the permalink of the article the address names when the reading shows the state (or EU wording) it pins and that state
 * holds the article, else the state's; the one the reading shows in its place when that is another version, as a stale
 * permalink's page shows (review of #959: any digest named itself canonical); for a refusal, the pinned state's permalink,
 * with no article (review of #967: any article segment named itself canonical), or for `pinned_digest_mismatch` the
 * current state's it names (R64); at an act's readable address, that address, its article's when the version shown holds
 * it (R67); at a permalink whose version is the one its act's readable address shows today (Luxembourg's date at the answer's
 * own instant), that readable address (R67: "identical version pages point their canonical link there").
 * `readingAddressShown`, which the page writes again once loaded.
 */
export function readingLawPath(initial) {
  const pinned = lawPathOf(initial.path);
  const outcome = readingOutcome(answerHandedWith('evidence_bundle', initial.envelope, { contract: LIVE_CONTRACT, objectType: 'evidence_bundle' }));
  return readingAddressShown(
    outcome.state === 'success' ? outcome.view : null,
    pinned,
    outcome.state === 'refusal' ? { code: outcome.code, payload: outcome.payload } : null,
    todayAt(initial.envelope?.context?.freshness?.observed_at),
  );
}

/**
 * What the page at a law's address says to search engines (R67; `search-engines.mjs`): `indexing`, `switch` when it shows
 * the version its act's readable address shows today (its canonical is that readable address), so the release's switch
 * may open it, else `never` (a refusal, or a version that is not today's: a superseded or later version stays out, and a
 * sitemap never lists its permalink); and `structuredData`, the schema.org Legislation markup of the version it shows, drawn
 * from the answer (`structured-data.mjs`), or null for a refusal.
 */
export function readingSearchEngines(initial) {
  const outcome = readingOutcome(answerHandedWith('evidence_bundle', initial.envelope, { contract: LIVE_CONTRACT, objectType: 'evidence_bundle' }));
  if (outcome.state !== 'success') return { indexing: 'never', structuredData: null };
  const lawPath = readingLawPath(initial);
  const pin = readingPinOf(initial.path, initial.envelope);
  const view = outcome.view;
  const versions = (view.publisher === 'eu-eurlex' ? view.wordings : view.states) ?? [];
  const version = versions.find((one) => (pin?.kind === 'state' ? one.permalink === pin.permalink : one.language === pin?.language)) ?? versions[0];
  return {
    indexing: parseSitePath(lawPath)?.kind === 'current' ? 'switch' : 'never',
    structuredData: version === undefined ? null : readingStructuredData({ view, version, canonical: lawPageLinks(lawPath, SITE_ORIGIN, CHROME_LOCALE).canonical }),
  };
}

/**
 * The page's own title at a law's address: the cited article's (R64: "the cited article first, with its own title") when
 * the address names one the version shown holds, else the reading page's. `citedTitle`, which the page writes again once
 * loaded.
 */
export function readingTitle(initial) {
  const outcome = readingOutcome(answerHandedWith('evidence_bundle', initial.envelope, { contract: LIVE_CONTRACT, objectType: 'evidence_bundle' }));
  return citedTitle(outcome, readingPinOf(initial.path, initial.envelope)) ?? liveChrome().reading.title;
}

/**
 * The page: its form in its idle state, as built for `reading.html`; or, given `initial`, the page at a law's address as
 * the server renders it from its own answer (R67: complete without JavaScript), with that answer in a data block beside
 * the root for the browser to hydrate from.
 */
export function renderLiveReadingPage({ initial = null } = {}) {
  const copy = liveChrome().reading;
  const searchEngines = initial === null ? { indexing: 'never', structuredData: null } : readingSearchEngines(initial);
  return renderHydratableDocument(
    <Document
      state="live-reading"
      title={initial === null ? copy.title : readingTitle(initial)}
      shell="ask"
      density={skinFor('ask').density}
      banner="live"
      lawPath={initial === null ? null : readingLawPath(initial)}
      indexing={searchEngines.indexing}
      structuredData={searchEngines.structuredData}
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <div id={LIVE_READING_ROOT}>{liveReadingTree(initial)}</div>
      {initial === null ? null : <script type="application/json" id={INITIAL_ANSWER_ID} dangerouslySetInnerHTML={{ __html: jsonInScript(initial.envelope) }} />}
      <script src={livePath('client-live-reading.js')} defer />
    </Document>,
  );
}
