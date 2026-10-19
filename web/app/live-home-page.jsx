// The home page, live: `/fr/` and `/en/` (the specification of record's R47; UX spec section 2, Screen 1).
//
// Built into `dist-live/{language}/index.html` (by `scripts/build-live.mjs`) as the page as built, which asks `coverage`
// once loaded; both servers render it instead from the API's coverage answer (`sitePageFor`, `ssr-entry.jsx`), with the
// facts it shows in a data block beside the root (`live-home.mjs`), which `client-live-home-entry.jsx` hydrates the same
// tree from. Either way the tree is `LiveHome`'s.

import { Document } from './Document.jsx';
import { LiveHome } from './LiveHome.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';
import { jsonInScript } from '../scripts/initial-answer.mjs';
import { HOME_FACTS_ID } from '../scripts/live-home.mjs';

export const LIVE_HOME_ROOT = 'live-home-root';

/** The tree the server renders and the browser hydrates: one and the same, from the same facts (or none). */
export function liveHomeTree(facts = null) {
  return <LiveHome contract={LIVE_CONTRACT} facts={facts} />;
}

/**
 * The home page: as built (`facts` null), or as the server renders it from its coverage answer, with the facts it shows
 * in a data block beside the root for the browser to hydrate from. Only a home page rendered from a coverage answer that
 * counts the mounted law is one the release's switch may open (R67): as built, or with no corpus mounted (review of #976,
 * F2), it is never indexed. `freshness` is what the coverage answer says of the law's freshness and commits (R87, R23).
 */
export function renderLiveHomePage({ facts = null, freshness = null } = {}) {
  const copy = liveChrome().home;
  return renderHydratableDocument(
    <Document
      state="live-home"
      title={copy.title}
      shell="ask"
      density={skinFor('ask').density}
      banner="live"
      indexing={facts?.coverage?.state === 'counted' ? 'switch' : 'never'}
      freshness={freshness}
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <div id={LIVE_HOME_ROOT}>{liveHomeTree(facts)}</div>
      {facts === null ? null : <script type="application/json" id={HOME_FACTS_ID} dangerouslySetInnerHTML={{ __html: jsonInScript(facts) }} />}
      <script src={livePath('client-live-home.js')} defer />
    </Document>,
  );
}
