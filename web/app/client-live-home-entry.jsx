// The live home page's browser entry, bundled to /{language}/client-live-home.js by `scripts/build-live.mjs`.
//
// A page the server rendered from its coverage answer carries the facts it shows: the same tree is rendered from them,
// so the page asks nothing as it loads. The page as built carries none, and asks once loaded (`LiveHome.jsx`).

import { attach } from './client.jsx';
import { LIVE_HOME_ROOT, liveHomeTree } from './live-home-page.jsx';
import { readHomeFacts } from '../scripts/live-home.mjs';

const root = document.getElementById(LIVE_HOME_ROOT);
if (root !== null) attach(root, liveHomeTree(readHomeFacts(document)));
