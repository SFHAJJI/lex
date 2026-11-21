// The side build's browser entry for the pieces that read the new engine's counts at run time (`NextPages.jsx`): How it
// works, Results, and "What the assistant reads" on Trust and Coverage. Bundled to /{language}/client-live-next.js by
// `scripts/build-live.mjs` and loaded by each of those pages. Each piece is built into its page in its loading state; this
// script draws it again in its own root, and the piece then asks the engine.

import { createRoot } from 'react-dom/client';
import { NEXT_CORPUS_ROOT, NEXT_HOW_ROOT, NEXT_RESULTS_ROOT, NextCorpus, NextResults } from './NextPages.jsx';
import { NEXT_DECISIONS_ROOT, NextDecisions, NextHowFunnel } from './NextHow.jsx';
import { NEXT_COVERAGE_ROOT, NextCoverage } from './NextCoverage.jsx';

// How it works is the funnel's page (`NextHow.jsx`); the earlier `NextHow` stays in `NextPages.jsx`, unwired.
for (const [id, element] of [[NEXT_HOW_ROOT, <NextHowFunnel />], [NEXT_RESULTS_ROOT, <NextResults />], [NEXT_CORPUS_ROOT, <NextCorpus />], [NEXT_COVERAGE_ROOT, <NextCoverage />], [NEXT_DECISIONS_ROOT, <NextDecisions />]]) {
  const root = document.getElementById(id);
  if (root !== null) createRoot(root).render(element);
}
