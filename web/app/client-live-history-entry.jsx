// The live provision history page's browser entry, bundled to /client-live-history.js by `scripts/build-live.mjs`.
//
// Separate from the preview pages' bundle, which never gains a request, as the other live entries are.

import { attach } from './client.jsx';
import { LIVE_HISTORY_ROOT, liveHistoryTree } from './live-history-page.jsx';

const root = document.getElementById(LIVE_HISTORY_ROOT);
if (root !== null) attach(root, liveHistoryTree());
