// The assistant dock's browser entry, bundled to /{language}/client-live-dock.js by `scripts/build-live.mjs` and loaded by
// every page that carries the dock (`Document`'s `dock`), beside that page's own script.

import { attach } from './client.jsx';
import { LIVE_DOCK_ROOT, liveDockTree } from './NextDock.jsx';

const root = document.getElementById(LIVE_DOCK_ROOT);
if (root !== null) attach(root, liveDockTree(root.getAttribute('data-dock-page')));
