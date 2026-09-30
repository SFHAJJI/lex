// The live export composer page's browser entry, bundled to /client-live-export.js by `scripts/build-live.mjs`.
//
// Separate from the preview pages' bundle, which never gains a request, as the other live entries are.

import { attach } from './client.jsx';
import { LIVE_EXPORT_ROOT, liveExportTree } from './live-export-page.jsx';

const root = document.getElementById(LIVE_EXPORT_ROOT);
if (root !== null) attach(root, liveExportTree());
