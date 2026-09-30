// The Trust and Coverage page, live: the first page that asks the mounted API.
//
// It is built into its own directory (`dist-live/`, by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure, because it is the one page whose content is not a
// fixture: served beside the API on one origin, it shows that server's answer. The server render is
// the loading state and `client-live-entry.jsx` hydrates the same tree, which then asks.

import { Document } from './Document.jsx';
import { LiveCoverage } from './LiveCoverage.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { contract } from '../../schemas/v3-platform/envelope-samples.json';

/** The contract the page reads envelopes against, from the census the platform renders. */
export const LIVE_CONTRACT = contract;

export const LIVE_COVERAGE_ROOT = 'live-coverage-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveCoverageTree() {
  return <LiveCoverage contract={LIVE_CONTRACT} />;
}

export function renderLiveCoveragePage() {
  return renderHydratableDocument(
    <Document
      state="live-coverage"
      title="Trust and Coverage"
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">Gateway</p>
      <h1>Trust and Coverage</h1>
      <p>
        What the corpus this server mounts holds and what it recorded as missing, asked of the
        server when the page loads. The request carries no query text.
      </p>
      <div id={LIVE_COVERAGE_ROOT}>{liveCoverageTree()}</div>
      <script src="/client-live.js" defer />
    </Document>,
  );
}
