// The reading page, live: the fourth page that asks the mounted API.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure. The server render is the form in its idle state and
// `client-live-reading-entry.jsx` hydrates the same tree; a reading is asked only when the reader
// submits.

import { Document } from './Document.jsx';
import { LiveReading } from './LiveReading.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';

export const LIVE_READING_ROOT = 'live-reading-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveReadingTree() {
  return <LiveReading contract={LIVE_CONTRACT} />;
}

export function renderLiveReadingPage() {
  return renderHydratableDocument(
    <Document
      state="live-reading"
      title="Reading"
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">Reading</p>
      <h1>The text on a date</h1>
      <p>
        The text of one Luxembourg work as it stood on one date, article by article, as the publisher
        wrote it, with what a quotation of it needs. The identifier and the date go to this server in
        the request and nowhere else, and this page keeps nothing.
      </p>
      <div id={LIVE_READING_ROOT}>{liveReadingTree()}</div>
      <script src="/client-live-reading.js" defer />
    </Document>,
  );
}
