// The assistant page's browser entry, bundled to /{language}/client-live-assistant.js by `scripts/build-live.mjs`. The server
// serves this script only while the assistant is switched on (V3WebRoot), as it serves the page.

import { attach } from './client.jsx';
import { LIVE_ASSISTANT_ROOT, liveAssistantTree } from './live-assistant-page.jsx';

const root = document.getElementById(LIVE_ASSISTANT_ROOT);
if (root !== null) attach(root, liveAssistantTree());
