// The live reading page's browser entry, bundled to /client-live-reading.js by `scripts/build-live.mjs`.
//
// Separate from the preview pages' bundle, which never gains a request, as the other live entries are.

import { attach } from './client.jsx';
import { LIVE_READING_ROOT, liveReadingTree } from './live-reading-page.jsx';
import { readInitialAnswer } from '../scripts/initial-answer.mjs';

// A page the server rendered from its own answer carries that answer: the same tree is rendered from it (R67).
const root = document.getElementById(LIVE_READING_ROOT);
if (root !== null) attach(root, liveReadingTree(readInitialAnswer(document, window.location)));
