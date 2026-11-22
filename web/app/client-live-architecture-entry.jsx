// The architecture dossier's browser entry (dossier-page, 22/11/2026): bundled to /{language}/client-live-architecture.js by
// `scripts/build-live.mjs` and loaded by `architecture.html` only. The dossier is built into its page in its loading state; this
// script draws it again in its own root, and the dossier then reads the live counts (GET only, no model call).

import { createRoot } from 'react-dom/client';
import { NEXT_ARCHITECTURE_ROOT, NextArchitecture } from './NextArchitecture.jsx';
import { followAnswers } from './freshness-client.jsx';

// The page carries no dock: its footer strip is drawn from the engine's index here, as on the other pages of the side build.
followAnswers(document);

const root = document.getElementById(NEXT_ARCHITECTURE_ROOT);
if (root !== null) createRoot(root).render(<NextArchitecture />);
