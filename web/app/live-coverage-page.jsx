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
import { contract } from '../../schemas/v3-platform/envelope-samples.json';
import censusCard from '../../schemas/v3-platform/evaluation-card.json';

/** The contract the page reads envelopes against, from the census the platform renders. */
export const LIVE_CONTRACT = contract;

export const LIVE_COVERAGE_ROOT = 'live-coverage-root';

/** The tree the server renders and the browser hydrates: one and the same. */
export function liveCoverageTree() {
  return <LiveCoverage contract={LIVE_CONTRACT} />;
}

/** The card the page carries when the build is handed none: the one the platform renders. */
export const CENSUS_EVALUATION_CARD = censusCard;

export function renderLiveCoveragePage({ card = CENSUS_EVALUATION_CARD } = {}) {
  const copy = liveChrome().coverage;
  const view = readEvaluationCard(card);
  return renderHydratableDocument(
    <Document
      state="live-coverage"
      title={copy.title}
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <div id={LIVE_COVERAGE_ROOT}>{liveCoverageTree()}</div>
      <EvaluationCardView view={view} />
      <script src={livePath('client-live.js')} defer />
    </Document>,
  );
}
