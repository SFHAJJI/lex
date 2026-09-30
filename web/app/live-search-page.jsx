// The search page, live: the second page that asks the mounted API.
//
// Built into `dist-live/` beside the Trust and Coverage page (by `scripts/build-live.mjs`), apart
// from the preview pages the browser gates measure. The server render is the form in its idle
// state and `client-live-search-entry.jsx` hydrates the same tree; a search is asked only when the
// reader submits.

import { Document } from './Document.jsx';
import { LiveSearch } from './LiveSearch.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';

export const LIVE_SEARCH_ROOT = 'live-search-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveSearchTree() {
  return <LiveSearch contract={LIVE_CONTRACT} />;
}

export function renderLiveSearchPage() {
  return renderHydratableDocument(
    <Document
      state="live-search"
      title="Search"
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">Search</p>
      <h1>Search the held text</h1>
      <p>
        Finds the articles whose text contains the phrase exactly as typed, or every word of it, in
        the text this server holds. The phrase goes to this server in the request and nowhere else,
        and this page keeps nothing.
      </p>
      <div id={LIVE_SEARCH_ROOT}>{liveSearchTree()}</div>
      <script src="/client-live-search.js" defer />
    </Document>,
  );
}
