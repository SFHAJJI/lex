// The text page, live: `text.html` under each interface language (the owner's brief of 2026-11-15, section 4). Built once
// per language by `scripts/build-live.mjs`; the same page for every text, which reads its query once hydrated
// (`TextPage.jsx`, `client-live-text-entry.jsx`). The assistant is docked beside it.

import { Document } from './Document.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { renderHydratableDocument } from './render-document.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';
import { LIVE_TEXT_ROOT, TextPage } from './TextPage.jsx';

export { LIVE_TEXT_ROOT };

/** The tree the server renders and the browser hydrates: one and the same, the loading state. */
export function liveTextTree() {
  return <TextPage contract={LIVE_CONTRACT} />;
}

export function renderLiveTextPage() {
  return renderHydratableDocument(
    <Document state="live-text" title={liveChrome().text.title} banner="live" layout="v4" dock>
      <div id={LIVE_TEXT_ROOT} className="lx-text-root">{liveTextTree()}</div>
      <script src={livePath('client-live-text.js')} defer />
    </Document>,
  );
}
