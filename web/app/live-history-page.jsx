// The provision history page, live: the fifth page that asks the mounted API.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure. The server render is the form in its idle state and
// `client-live-history-entry.jsx` hydrates the same tree; a lineage is asked only when the reader
// submits.

import { Document } from './Document.jsx';
import { liveChrome } from '../scripts/live-chrome.mjs';
import { LiveHistory } from './LiveHistory.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';

export const LIVE_HISTORY_ROOT = 'live-history-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveHistoryTree() {
  return <LiveHistory contract={LIVE_CONTRACT} />;
}

export function renderLiveHistoryPage() {
  const copy = liveChrome().history;
  return renderHydratableDocument(
    <Document
      state="live-history"
      title={copy.title}
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <div id={LIVE_HISTORY_ROOT}>{liveHistoryTree()}</div>
      <script src="/client-live-history.js" defer />
    </Document>,
  );
}
