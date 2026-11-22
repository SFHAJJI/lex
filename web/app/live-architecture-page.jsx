// The architecture dossier, live: `architecture.html` under each interface language (dossier-page, 22/11/2026), in the V4
// frame and menu, like Results and How it works: no dock, no skin of its own. Built once per language by
// `scripts/build-live.mjs` in its loading state; the live counts are asked once the page is drawn (`NextArchitecture.jsx`,
// `client-live-architecture-entry.jsx`). It asks for no answer, so its footer says it carries none.

import { Document } from './Document.jsx';
import { CHROME_LOCALE, livePath } from '../scripts/live-chrome.mjs';
import { dossierCopy } from '../scripts/dossier-copy.mjs';
import { renderDocument } from './render-document.mjs';
import { ARCHITECTURE_PAGE, ARCHITECTURE_SCRIPT, NEXT_ARCHITECTURE_ROOT, NextArchitecture } from './NextArchitecture.jsx';

/** The page's file under each interface language, and its state. */
export const ARCHITECTURE_STATE = 'live-architecture';
export { ARCHITECTURE_PAGE };

export function renderLiveArchitecturePage() {
  return renderDocument(
    <Document state={ARCHITECTURE_STATE} title={dossierCopy(CHROME_LOCALE).title} banner="live" layout="v4" localePage={ARCHITECTURE_PAGE} answers={false}>
      <div id={NEXT_ARCHITECTURE_ROOT}><NextArchitecture /></div>
      <script src={livePath(ARCHITECTURE_SCRIPT)} defer />
    </Document>,
  );
}
