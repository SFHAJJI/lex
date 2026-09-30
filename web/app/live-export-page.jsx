// The export composer page, live: the eighth page that asks the mounted API.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure. The server render is the form in its idle state and
// `client-live-export-entry.jsx` hydrates the same tree; the reading is asked only when the reader
// submits, and pinning and saving ask nothing.

import { Document } from './Document.jsx';
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
  return renderHydratableDocument(
    <Document
      state="live-export"
      title="Export composer"
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">Export composer</p>
      <h1>Take articles away, with their citations</h1>
      <p>
        Read one Luxembourg work on one date, pin the articles you need, and save them as JSON or CSV.
        Each exported article carries its citation, its text digest, its official source and the rights
        it was served under, and every export carries the watermark. The identifier and the date go to
        this server in the request and nowhere else; the file is made in this page, and this page keeps
        nothing.
      </p>
      <div id={LIVE_EXPORT_ROOT}>{liveExportTree()}</div>
      <script src="/client-live-export.js" defer />
    </Document>,
  );
}
