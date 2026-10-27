// The compare page, live: the sixth page that asks the mounted API.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure. The page as built is the form in its idle state; at a comparison's address
// (`/en/compare.html?work=…&from=…&to=…`, R57 and R67) both servers render it from their own answer to `diff`
// (`sitePageFor`, `ssr-entry.jsx`), complete without JavaScript, and `client-live-compare-entry.jsx` hydrates the same tree
// from the answer the page carries.

import { Document } from './Document.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { LiveCompare } from './LiveCompare.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';
import { INITIAL_ANSWER_ID, jsonInScript } from '../scripts/initial-answer.mjs';
import { freshnessFacts } from '../scripts/freshness.mjs';

export const LIVE_COMPARE_ROOT = 'live-compare-root';

/**
 * The tree the server renders and the browser hydrates: one and the same. `initial`, when the server rendered the page from
 * its own answer at a comparison's address, is that answer and the address it answered (its path and query).
 */
export function liveCompareTree(initial = null) {
  return <LiveCompare contract={LIVE_CONTRACT} initial={initial} />;
}

/**
 * The page: its form in its idle state, as built for `compare.html`; or, given `initial`, the comparison at its address as
 * the server renders it from its own answer, with that answer in a data block beside the root for the browser to hydrate
 * from. A comparison is never offered to search engines.
 */
export function renderLiveComparePage({ initial = null } = {}) {
  const copy = liveChrome().compare;
  return renderHydratableDocument(
    <Document
      state="live-compare"
      title={copy.title}
      shell="ask"
      density={skinFor('ask').density}
      banner="live"
      freshness={initial === null ? null : freshnessFacts(initial.envelope)}
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <div id={LIVE_COMPARE_ROOT}>{liveCompareTree(initial)}</div>
      {initial === null ? null : <script type="application/json" id={INITIAL_ANSWER_ID} dangerouslySetInnerHTML={{ __html: jsonInScript(initial.envelope) }} />}
      <script src={livePath('client-live-compare.js')} defer />
    </Document>,
  );
}
