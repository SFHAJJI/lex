// The page a reader gets at an address the site has no page for (the specification of record's R60: "A 404 page offers
// search"; plan item 6.9: it never looks like a legal answer).
//
// It says plainly that there is no page at the address, and that this is about the site's pages and not about the law,
// then offers the home page's search: the same search screen, hydrated by the same script (`client-live-search.js`), so
// a phrase goes to the server in a request body and never into an address, as on the home page. It names no address of
// its own (no alternate-language links, no canonical) and repeats nothing of the address asked.
//
// Built into `dist-live/not-found/{language}.html`, once per interface language, by `scripts/build-live.mjs`; neither server
// serves it at that address. Both answer a page request nothing claims with it, with status 404, in the reader's
// language (`scripts/not-found.mjs`, `V3WebRoot.NotFound.cs`), so the page stands at whatever address was asked: its
// stylesheet and script are named from the root.

import { Document } from './Document.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { LIVE_SEARCH_ROOT, liveSearchTree } from './live-search-page.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';

export function renderLiveNotFoundPage() {
  const copy = liveChrome().notFound;
  return renderHydratableDocument(
    <Document
      state="live-not-found"
      title={copy.title}
      shell="ask"
      density={skinFor('ask').density}
      banner="live"
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <p data-not-found-home="">
        <a href={livePath('index.html')}>{copy.home}</a>
      </p>
      <div id={LIVE_SEARCH_ROOT}>{liveSearchTree()}</div>
      <script src={livePath('client-live-search.js')} defer />
    </Document>,
  );
}
