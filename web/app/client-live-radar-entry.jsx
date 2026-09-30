// The live change radar page's browser entry, bundled to /client-live-radar.js by `scripts/build-live.mjs`.
//
// Separate from the preview pages' bundle, which never gains a request, as the other live entries are.

import { attach } from './client.jsx';
import { LIVE_RADAR_ROOT, liveRadarTree } from './live-radar-page.jsx';

const root = document.getElementById(LIVE_RADAR_ROOT);
if (root !== null) attach(root, liveRadarTree());
