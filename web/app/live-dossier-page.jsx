// The dossier page, live: the third page that asks the mounted API.
//
// Built into `dist-live/` beside Trust and Coverage and search (by `scripts/build-live.mjs`), apart
// from the preview pages the browser gates measure. The server render is the form in its idle state
// and `client-live-dossier-entry.jsx` hydrates the same tree; a dossier is asked only when the reader
// submits.

import { Document } from './Document.jsx';
import { LiveDossier } from './LiveDossier.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';

export const LIVE_DOSSIER_ROOT = 'live-dossier-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveDossierTree() {
  return <LiveDossier contract={LIVE_CONTRACT} />;
}

export function renderLiveDossierPage() {
  return renderHydratableDocument(
    <Document
      state="live-dossier"
      title="Dossier"
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">Dossier</p>
      <h1>A work's dossier</h1>
      <p>
        What this server holds for one Luxembourg work: its titles, its publisher-dated states and
        what the dossier does not hold. The identifier goes to this server in the request and nowhere
        else, and this page keeps nothing.
      </p>
      <div id={LIVE_DOSSIER_ROOT}>{liveDossierTree()}</div>
      <script src="/client-live-dossier.js" defer />
    </Document>,
  );
}
