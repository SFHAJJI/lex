// The live page's browser entry, bundled to /client-live.js by `scripts/build-live.mjs`.
//
// Separate from `client-entry.jsx` on purpose: the preview pages' bundle never gains a request,
// which the browser gate holds, and only the page built to be served beside the API ships one.

import { attach } from './client.jsx';
import { LIVE_COVERAGE_ROOT, liveCoverageTree } from './live-coverage-page.jsx';

const root = document.getElementById(LIVE_COVERAGE_ROOT);
if (root !== null) attach(root, liveCoverageTree());
