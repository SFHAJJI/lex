// The export composer page, live: the eighth page that asks the mounted API.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure. The server render is the form in its idle state and
// `client-live-export-entry.jsx` hydrates the same tree; the reading is asked only when the reader
// submits, and pinning and saving ask nothing.

import { Document } from './Document.jsx';
import { liveChrome } from '../scripts/live-chrome.mjs';
import { LiveExport } from './LiveExport.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';

export const LIVE_EXPORT_ROOT = 'live-export-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveExportTree() {
  return <LiveExport contract={LIVE_CONTRACT} />;
}

export function renderLiveExportPage() {
  const copy = liveChrome().export;
  return renderHydratableDocument(
    <Document
      state="live-export"
      title={copy.title}
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <div id={LIVE_EXPORT_ROOT}>{liveExportTree()}</div>
      <script src="/client-live-export.js" defer />
    </Document>,
  );
}
