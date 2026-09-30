// The reading page, live: the fourth page that asks the mounted API.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure. The server render is the form in its idle state and
// `client-live-reading-entry.jsx` hydrates the same tree; a reading is asked only when the reader
// submits.

import { Document } from './Document.jsx';
import { liveChrome } from '../scripts/live-chrome.mjs';
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
  const copy = liveChrome().reading;
  return renderHydratableDocument(
    <Document
      state="live-reading"
      title={copy.title}
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <div id={LIVE_READING_ROOT}>{liveReadingTree()}</div>
      <script src="/client-live-reading.js" defer />
    </Document>,
  );
}
