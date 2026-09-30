// The compare page, live: the sixth page that asks the mounted API.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure. The server render is the form in its idle state and
// `client-live-compare-entry.jsx` hydrates the same tree; a comparison is asked only when the reader
// submits.

import { Document } from './Document.jsx';
import { LiveCompare } from './LiveCompare.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';

export const LIVE_COMPARE_ROOT = 'live-compare-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveCompareTree() {
  return <LiveCompare contract={LIVE_CONTRACT} />;
}

export function renderLiveComparePage() {
  return renderHydratableDocument(
    <Document
      state="live-compare"
      title="Compare"
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">Compare</p>
      <h1>Two states, article by article</h1>
      <p>
        The states of one Luxembourg work that applied on two dates, compared by the publisher's
        article ids and wording, with nothing said about legal effect. The identifier and the dates go
        to this server in the request and nowhere else, and this page keeps nothing.
      </p>
      <div id={LIVE_COMPARE_ROOT}>{liveCompareTree()}</div>
      <script src="/client-live-compare.js" defer />
    </Document>,
  );
}
