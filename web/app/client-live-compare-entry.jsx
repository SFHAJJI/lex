// The live compare page's browser entry, bundled to /client-live-compare.js by `scripts/build-live.mjs`.
//
// Separate from the preview pages' bundle, which never gains a request, as the other live entries are.

import { attach } from './client.jsx';
import { LIVE_COMPARE_ROOT, liveCompareTree } from './live-compare-page.jsx';

const root = document.getElementById(LIVE_COMPARE_ROOT);
if (root !== null) attach(root, liveCompareTree());
