// The text page's browser entry, bundled to /{language}/client-live-text.js by `scripts/build-live.mjs`.

import { attach } from './client.jsx';
import { LIVE_TEXT_ROOT, liveTextTree } from './live-text-page.jsx';

const root = document.getElementById(LIVE_TEXT_ROOT);
if (root !== null) attach(root, liveTextTree());
