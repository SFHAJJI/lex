// The live dossier page's browser entry, bundled to /client-live-dossier.js by `scripts/build-live.mjs`.
//
// Separate from the preview pages' bundle, which never gains a request, as the other live entries are.

import { attach } from './client.jsx';
import { LIVE_DOSSIER_ROOT, liveDossierTree } from './live-dossier-page.jsx';
import { readInitialAnswer } from '../scripts/initial-answer.mjs';

// A page the server rendered from its own answer carries that answer: the same tree is rendered from it (R67).
const root = document.getElementById(LIVE_DOSSIER_ROOT);
if (root !== null) attach(root, liveDossierTree(readInitialAnswer(document, window.location)));
