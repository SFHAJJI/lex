// The change radar page, live: the seventh page that asks the mounted API.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure. The server render is the form in its idle state and
// `client-live-radar-entry.jsx` hydrates the same tree; a radar is asked only when the reader submits.

import { Document } from './Document.jsx';
import { LiveRadar } from './LiveRadar.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';

export const LIVE_RADAR_ROOT = 'live-radar-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveRadarTree() {
  return <LiveRadar contract={LIVE_CONTRACT} />;
}

export function renderLiveRadarPage() {
  return renderHydratableDocument(
    <Document
      state="live-radar"
      title="Radar"
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">Radar</p>
      <h1>The change radar</h1>
      <p>
        The publisher-dated states of Luxembourg works that this server holds in a window of dates,
        each with the state it replaced and whether its wording changed. The dates and any identifier
        go to this server in the request and nowhere else, and this page keeps nothing.
      </p>
      <div id={LIVE_RADAR_ROOT}>{liveRadarTree()}</div>
      <script src="/client-live-radar.js" defer />
    </Document>,
  );
}
