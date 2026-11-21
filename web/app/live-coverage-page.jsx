// The Trust and Coverage page, live: the first page that asks the mounted API.
//
// It is built into its own directory (`dist-live/`, by `scripts/build-live.mjs`), apart from the
// preview pages the browser gates measure, because it is the one page whose content is not a
// fixture: served beside the API on one origin, it shows that server's answer. The server render is
// the loading state and `client-live-entry.jsx` hydrates the same tree, which then asks.
//
// Below the answer, the page carries the evaluation card (Decision 95, ruling 2), read by
// `readEvaluationCard` and rendered by the server alone, outside the hydrated tree: it is the card the
// build was given, not an answer of the server, so it needs no script and asks nothing. The build
// gives it the card the platform renders (`schemas/v3-platform/evaluation-card.json`) unless it is
// handed another, as a release build will be.

import { Document } from './Document.jsx';
import { liveChrome, livePath } from '../scripts/live-chrome.mjs';
import { LiveCoverage } from './LiveCoverage.jsx';
import { renderHydratableDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { EvaluationCardView } from './EvaluationCardView.jsx';
import { readEvaluationCard } from '../scripts/evaluation-card.mjs';
import { contract } from '../schemas/v3-platform/envelope-samples.json';
import censusCard from '../schemas/v3-platform/evaluation-card.json';
import { NEXT_CORPUS_ROOT, NEXT_SCRIPT, NextCorpus } from './NextPages.jsx';
import { NEXT_COVERAGE_ROOT, NextCoverage } from './NextCoverage.jsx';

/** The contract the page reads envelopes against, from the census the platform renders. */
export const LIVE_CONTRACT = contract;

export const LIVE_COVERAGE_ROOT = 'live-coverage-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveCoverageTree() {
  return <LiveCoverage contract={LIVE_CONTRACT} />;
}

/** The card the page carries when the build is handed none: the one the platform renders. */
export const CENSUS_EVALUATION_CARD = censusCard;

// Release-a (21/11/2026): the page carries only the new engine's own counts and what it does not hold. The earlier platform's
// coverage answer ("what this mount holds", a fixture's evaluation card in English) described another engine, so neither is
// drawn any more; `liveCoverageTree` and the card stay exported for the code that still imports them.
export function renderLiveCoveragePage({ card = CENSUS_EVALUATION_CARD } = {}) {
  const copy = liveChrome().coverage;
  readEvaluationCard(card);
  return renderHydratableDocument(
    <Document
      state="live-coverage"
      title={copy.title}
      shell="ask"
      density={skinFor('ask').density}
      banner="live"
      layout="v4"
      prose
      indexing="switch"
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      {/* What Lex holds and does not, by type, by name and by date, from the data builder's file (`NextCoverage.jsx`): drawn once
          the file is read, nothing while there is none. */}
      <div id={NEXT_COVERAGE_ROOT}><NextCoverage /></div>
      {/* The side build: what the new engine reads, from its own counts, asked once the page is drawn (`NextPages.jsx`). */}
      <div id={NEXT_CORPUS_ROOT}><NextCorpus /></div>
      <script src={livePath(NEXT_SCRIPT)} defer />
    </Document>,
  );
}
