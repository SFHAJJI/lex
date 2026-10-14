// The search page, live: the second page that asks the mounted API.
//
// Built into `dist-live/` beside the Trust and Coverage page (by `scripts/build-live.mjs`), apart
// from the preview pages the browser gates measure. The server render is the form in its idle
// state and `client-live-search-entry.jsx` hydrates the same tree; a search is asked only when the
// reader submits.

import { Document } from './Document.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { LiveSearch } from './LiveSearch.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';

export const LIVE_SEARCH_ROOT = 'live-search-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveSearchTree() {
  return <LiveSearch contract={LIVE_CONTRACT} />;
}

/** The search page; `home` renders it as the site's home page, under the site's introduction (UX spec section 2). */
function renderSearchDocument({ home }) {
  const copy = home ? liveChrome().home : liveChrome().search;
  return renderHydratableDocument(
    <Document
      state={home ? 'live-home' : 'live-search'}
      title={copy.title}
      shell="ask"
      density={skinFor('ask').density}
      banner="live"
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <div id={LIVE_SEARCH_ROOT}>{liveSearchTree()}</div>
      <script src={livePath('client-live-search.js')} defer />
    </Document>,
  );
}

export function renderLiveSearchPage() {
  return renderSearchDocument({ home: false });
}

/** The home page: the same search, hydrated by the same script, under the site's own heading. */
export function renderLiveHomePage() {
  return renderSearchDocument({ home: true });
}
