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
import { INITIAL_ANSWER_ID, jsonInScript, lawPathOf, readingAddressShown } from '../scripts/initial-answer.mjs';
import { readingOutcome } from '../scripts/live-reading.mjs';
import { answerHandedWith } from '../scripts/v3-client.mjs';

export const LIVE_READING_ROOT = 'live-reading-root';

/**
 * The tree the server renders and the browser hydrates: one and the same. `initial`, when the server rendered the page
 * from its own answer at a law's address (`initial-answer.mjs`), is that answer and the path it answered.
 */
export function liveReadingTree(initial = null) {
  return <LiveReading contract={LIVE_CONTRACT} initial={initial} />;
}

/**
 * The address the page at a permalink names as its own (canonical, and in each language): the permalink when the reading
 * shows the state (or EU wording) it pins, its article included; else the one the reading shows in its place, as a stale
 * permalink's page shows another version (review of #959: any digest named itself canonical); else, for a refusal, the
 * address it was opened at (`readingAddressShown`, which the page writes again once loaded).
 */
export function readingLawPath(initial) {
  const pinned = lawPathOf(initial.path);
  const outcome = readingOutcome(answerHandedWith('evidence_bundle', initial.envelope, { contract: LIVE_CONTRACT, objectType: 'evidence_bundle' }));
  return outcome.state === 'success' ? readingAddressShown(outcome.view, pinned) : pinned;
}

/**
 * The page: its form in its idle state, as built for `reading.html`; or, given `initial`, the page at a law's address as
 * the server renders it from its own answer (R67: complete without JavaScript), with that answer in a data block beside
 * the root for the browser to hydrate from.
 */
export function renderLiveReadingPage({ initial = null } = {}) {
  const copy = liveChrome().reading;
  return renderHydratableDocument(
    <Document
      state="live-reading"
      title={copy.title}
      shell="ask"
      density={skinFor('ask').density}
      banner="live"
      lawPath={initial === null ? null : readingLawPath(initial)}
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
