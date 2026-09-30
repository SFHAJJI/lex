// The change radar page, live: the seventh page that asks the mounted API.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure. The server render is the form in its idle state and
// `client-live-radar-entry.jsx` hydrates the same tree; a radar is asked only when the reader submits.

import { Document } from './Document.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
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
  const copy = liveChrome().radar;
  return renderHydratableDocument(
    <Document
      state="live-radar"
      title={copy.title}
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <div id={LIVE_RADAR_ROOT}>{liveRadarTree()}</div>
      <script src={livePath('client-live-radar.js')} defer />
    </Document>,
  );
}
