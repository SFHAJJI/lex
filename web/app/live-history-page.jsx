// The provision history page, live: the fifth page that asks the mounted API.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure. The server render is the form in its idle state and
// `client-live-history-entry.jsx` hydrates the same tree; a lineage is asked only when the reader
// submits.

import { Document } from './Document.jsx';
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
  return renderHydratableDocument(
    <Document
      state="live-history"
      title="Provision history"
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">Provision history</p>
      <h1>One article through its states</h1>
      <p>
        Which held states of one Luxembourg work carry the publisher's article id, whether its
        wording changed from one state to the next, and which held states do not carry it. The
        identifier and the article id go to this server in the request and nowhere else, and this
        page keeps nothing.
      </p>
      <div id={LIVE_HISTORY_ROOT}>{liveHistoryTree()}</div>
      <script src="/client-live-history.js" defer />
    </Document>,
  );
}
