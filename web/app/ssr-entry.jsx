// The server renderer: the dossier and reading pages at a law's address, rendered by the API from its own answer (the
// specification of record's R67: complete HTML from the server, readable without JavaScript, then enhanced), the home
// page, rendered from its coverage answer (`live-home.mjs`), and the Developers page, from its answer to browse for the
// first act it holds, its live examples (`developers.mjs`).
//
// Bundled once per interface language into `ssr/{language}.js` (`build-live.mjs`), as a classic script for an engine
// with no module loader, no DOM and no network (the API runs it in V8, `V3PageRenderer`; the live server in a Node
// context). It names one function, `lexSsr.render(kind, envelopeJson, path)`, which renders the page exactly as the
// browser will hydrate it (`initial-answer.mjs`). It reads nothing and writes nothing but its return value.

import { renderLiveDossierPage } from './live-dossier-page.jsx';
import { renderLiveReadingPage } from './live-reading-page.jsx';
import { renderLiveHomePage } from './live-home-page.jsx';
import { LIVE_CONTRACT } from './live-coverage-page.jsx';
import { renderLiveReferencePage } from './ReferencePages.jsx';
import { homeFacts } from '../scripts/live-home.mjs';
import { freshnessFacts } from '../scripts/freshness.mjs';

// The Developers page's contracts (`developersModel`), read by the build and handed in as data (`build-live.mjs`), since this
// renderer reads nothing; a renderer built without them renders no Developers page, which is then served as built.
/* global __LEX_DEVELOPERS__ */
const DEVELOPERS = typeof __LEX_DEVELOPERS__ === 'undefined' ? null : __LEX_DEVELOPERS__;

globalThis.lexSsr = Object.freeze({
  render(kind, envelopeJson, path) {
    const initial = { envelope: JSON.parse(envelopeJson), path };
    if (kind === 'reading') return renderLiveReadingPage({ initial });
    if (kind === 'dossier') return renderLiveDossierPage({ initial });
    if (kind === 'home') return renderLiveHomePage({ facts: homeFacts(initial.envelope, LIVE_CONTRACT), freshness: freshnessFacts(initial.envelope) });
    // The Developers page, with its live examples from this server's own answer to browse (the plan's item 8.4).
    if (kind === 'developers') return renderLiveReferencePage('developers', { developers: DEVELOPERS, live: initial.envelope });
    throw new Error(`there is no server-rendered ${kind} page`);
  },
});
