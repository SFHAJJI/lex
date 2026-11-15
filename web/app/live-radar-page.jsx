// The Changes page, live (the owner's brief of 2026-11-15, V4Changes), in the V4 layout with the assistant docked beside it.
//
// Built into `dist-live/` beside the other live pages (by `scripts/build-live.mjs`). The server render is the heading, the
// controls and the loading line; `client-live-radar-entry.jsx` hydrates the same tree, which asks the last 30 days once this
// browser knows today in Luxembourg.

import { Document } from './Document.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { LiveRadar } from './LiveRadar.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';

export const LIVE_RADAR_ROOT = 'live-radar-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveRadarTree() {
  return <LiveRadar contract={LIVE_CONTRACT} />;
}

export function renderLiveRadarPage() {
  const copy = liveChrome().radar;
  return renderHydratableDocument(
    <Document state="live-radar" title={copy.title} banner="live" layout="v4" dock>
      <div className="lx-changes">
        <h1>{copy.heading}</h1>
        <p>{copy.intro}</p>
        <div id={LIVE_RADAR_ROOT}>{liveRadarTree()}</div>
      </div>
      <script src={livePath('client-live-radar.js')} defer />
    </Document>,
  );
}
