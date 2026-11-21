// The side build's Results page, live: `results.html` under each interface language, in the V4 frame and the reading
// measure of the earlier pages (`.lx-prose`). Built once per language by `scripts/build-live.mjs` in its loading state; the
// scored test is asked once the page is drawn (`GET /api/next/results`; `NextPages.jsx`, `client-live-next-entry.jsx`).
// It asks the current site's API nothing, so its header and footer are those of a page that carries no answer.

import { Document } from './Document.jsx';
import { livePath } from '../scripts/live-chrome.mjs';
import { nextCopy } from '../scripts/next-copy.mjs';
import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';
import { renderDocument } from './render-document.mjs';
import { NEXT_RESULTS_ROOT, NEXT_SCRIPT, NextResults } from './NextPages.jsx';

/** The page's file under each interface language, and its state. */
export const RESULTS_PAGE = 'results.html';
export const RESULTS_STATE = 'live-results';

export function renderLiveResultsPage() {
  return renderDocument(
    <Document state={RESULTS_STATE} title={nextCopy(CHROME_LOCALE).results.title} banner="live" layout="v4" prose localePage={RESULTS_PAGE} answers={false}>
      <div id={NEXT_RESULTS_ROOT}><NextResults /></div>
      <script src={livePath(NEXT_SCRIPT)} defer />
    </Document>,
  );
}
