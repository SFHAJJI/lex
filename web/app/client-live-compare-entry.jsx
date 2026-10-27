// The live compare page's browser entry, bundled to /client-live-compare.js by `scripts/build-live.mjs`.
//
// Separate from the preview pages' bundle, which never gains a request, as the other live entries are.

import { attach } from './client.jsx';
import { LIVE_COMPARE_ROOT, liveCompareTree } from './live-compare-page.jsx';
import { readInitialAnswer } from '../scripts/initial-answer.mjs';

// A page the server rendered from its own answer carries that answer: the same tree is rendered from it, at the address
// the page stands at, its query included, as the server rendered it (R57, R67).
const root = document.getElementById(LIVE_COMPARE_ROOT);
const initial = readInitialAnswer(document, window.location);
if (root !== null) attach(root, liveCompareTree(initial === null ? null : { ...initial, path: window.location.pathname + window.location.search }));
