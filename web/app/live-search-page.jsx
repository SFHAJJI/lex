// The search page, live: the second page that asks the mounted API.
//
// Built into `dist-live/` beside the Trust and Coverage page (by `scripts/build-live.mjs`), apart
// from the preview pages the browser gates measure. The server render is the form in its idle
// state and `client-live-search-entry.jsx` hydrates the same tree; a search is asked only when the
// reader submits. The home page is its own page (`live-home-page.jsx`), whose search is the home
// form of the same screen; the 404 page offers that home form (`live-not-found-page.jsx`). The root
// names its form (`data-variant`), so the one script hydrates either, and whether the form keeps its search in the page's
// address (`data-address`: the 404 page's keeps none, since it stands at an address this site has no page for).

import { Document } from './Document.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { LiveSearch } from './LiveSearch.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';

export const LIVE_SEARCH_ROOT = 'live-search-root';

/** The tree the server renders and the browser hydrates: one and the same, in the form the root names. */
export function liveSearchTree(variant = 'search', addressed = true) {
  return <LiveSearch contract={LIVE_CONTRACT} variant={variant} addressed={addressed} />;
}

/** The search screen's root, naming its form and whether it keeps its search in the address, around its tree. */
export function LiveSearchRoot({ variant = 'search', addressed = true }) {
  return <div id={LIVE_SEARCH_ROOT} data-variant={variant} data-address={addressed ? 'kept' : 'none'}>{liveSearchTree(variant, addressed)}</div>;
}

/** The search page: the search form with the optional work identifier. */
export function renderLiveSearchPage() {
  const copy = liveChrome().search;
  return renderHydratableDocument(
    <Document
      state="live-search"
      title={copy.title}
      shell="ask"
      density={skinFor('ask').density}
      banner="live"
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <LiveSearchRoot variant="search" />
      <script src={livePath('client-live-search.js')} defer />
    </Document>,
  );
}
