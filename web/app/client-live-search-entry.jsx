// The live search page's browser entry, bundled to /client-live-search.js by `scripts/build-live.mjs`.
//
// Separate from the preview pages' bundle, which never gains a request, as the live coverage entry is.

import { attach } from './client.jsx';
import { LIVE_SEARCH_ROOT, liveSearchTree } from './live-search-page.jsx';

const root = document.getElementById(LIVE_SEARCH_ROOT);
if (root !== null) attach(root, liveSearchTree());
