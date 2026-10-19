// The dossier page, live: the third page that asks the mounted API.
//
// Built into `dist-live/` beside Trust and Coverage and search (by `scripts/build-live.mjs`), apart
// from the preview pages the browser gates measure. The server render is the form in its idle state
// and `client-live-dossier-entry.jsx` hydrates the same tree; a dossier is asked only when the reader
// submits.

import { freshnessFacts } from '../scripts/freshness.mjs';
import { Document } from './Document.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { LiveDossier } from './LiveDossier.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';
import { INITIAL_ANSWER_ID, jsonInScript, lawPathOf } from '../scripts/initial-answer.mjs';
import { dossierOutcome } from '../scripts/live-dossier.mjs';
import { answerHandedWith } from '../scripts/v3-client.mjs';
import { CHROME_LOCALE } from '../scripts/live-chrome.mjs';
import { lawPageLinks } from '../scripts/permalinks.mjs';
import { dossierStructuredData } from '../scripts/structured-data.mjs';
import { SITE_ORIGIN } from './Document.jsx';

export const LIVE_DOSSIER_ROOT = 'live-dossier-root';

/**
 * The tree the server renders and the browser hydrates: one and the same. `initial`, when the server rendered the page
 * from its own answer at a law's address (`initial-answer.mjs`), is that answer and the path it answered.
 */
export function liveDossierTree(initial = null) {
  return <LiveDossier contract={LIVE_CONTRACT} initial={initial} />;
}

/**
 * What the dossier at a work's address says to search engines (R67; `search-engines.mjs`): `indexing`, `switch` for a
 * dossier the answer gives, so the release's switch may open it, `never` for a refusal (a work the index does not hold, no
 * corpus mounted); and `structuredData`, the work's schema.org Legislation markup drawn from the answer
 * (`structured-data.mjs`), or null for a refusal.
 */
export function dossierSearchEngines(initial) {
  const outcome = dossierOutcome(answerHandedWith('dossier', initial.envelope, { contract: LIVE_CONTRACT, objectType: 'work_record' }));
  if (outcome.state !== 'success') return { indexing: 'never', structuredData: null };
  const canonical = lawPageLinks(lawPathOf(initial.path), SITE_ORIGIN, CHROME_LOCALE).canonical;
  return { indexing: 'switch', structuredData: dossierStructuredData({ view: outcome.view, canonical }) };
}

/**
 * The page: its form in its idle state, as built for `dossier.html`; or, given `initial`, the page at a law's address as
 * the server renders it from its own answer (R67: complete without JavaScript), with that answer in a data block beside
 * the root for the browser to hydrate from.
 */
export function renderLiveDossierPage({ initial = null } = {}) {
  const copy = liveChrome().dossier;
  const searchEngines = initial === null ? { indexing: 'never', structuredData: null } : dossierSearchEngines(initial);
  return renderHydratableDocument(
    <Document
      state="live-dossier"
      title={copy.title}
      shell="ask"
      density={skinFor('ask').density}
      banner="live"
      lawPath={initial === null ? null : lawPathOf(initial.path)}
      indexing={searchEngines.indexing}
      structuredData={searchEngines.structuredData}
      freshness={initial === null ? null : freshnessFacts(initial.envelope)}
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <div id={LIVE_DOSSIER_ROOT}>{liveDossierTree(initial)}</div>
      {initial === null ? null : <script type="application/json" id={INITIAL_ANSWER_ID} dangerouslySetInnerHTML={{ __html: jsonInScript(initial.envelope) }} />}
      <script src={livePath('client-live-dossier.js')} defer />
    </Document>,
  );
}
