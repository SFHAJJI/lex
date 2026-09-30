// The Trust and Coverage screen, asked of the live API.
//
// The server renders the loading state and the browser asks, in an effect, so hydration changes
// nothing the server rendered (the rule `client.jsx` enforces) and the answer arrives as a state
// change afterwards. What the answer means is decided elsewhere: `scripts/live-coverage.mjs` maps it
// to a view state, `Coverage` lays out a coverage answer and `RefusalCard` a refusal. This file only
// chooses which one, and marks the state on the root (`data-answer-state`) so a browser run can wait
// for the answer rather than for a clock.

import { useEffect, useState } from 'react';

import { Coverage } from './Coverage.jsx';
import { RefusalCard } from './RefusalCard.jsx';
import { LIVE_COVERAGE_LOADING, startLiveCoverage } from '../scripts/live-coverage.mjs';
import { LiveAnswer } from './LiveAnswer.jsx';

const LOADING = Object.freeze({ state: 'loading', sentence: LIVE_COVERAGE_LOADING });

/** One view state, laid out: the answer, the refusal card, or the sentence a state carries. */
export function CoverageAnswerView({ outcome }) {
  if (outcome.state === 'success') {
    return (
      <section data-answer-state="success">
        <Coverage answer={outcome.answer} />
      </section>
    );
  }

  if (outcome.state === 'refusal' && outcome.card) {
    return (
      <section data-answer-state="refusal">
        <RefusalCard code={outcome.code} sentence={outcome.sentence} payload={outcome.payload} />
      </section>
    );
  }

  return (
    <section data-answer-state={outcome.state}>
      <p role="status">{outcome.sentence}</p>
    </section>
  );
}

/**
 * The live screen: loading until the one request settles, then its state. The request is asked once
 * per mount (`startLiveCoverage`) and abandoned when the screen unmounts; a failure is shown, never
 * retried behind the reader's back. `contract` and `fetchImpl` must be stable for the screen's life:
 * a new identity asks again (while the previous state stays on screen until the new one settles).
 */
export function LiveCoverage({ contract, fetchImpl }) {
  const [outcome, setOutcome] = useState(LOADING);
  useEffect(() => startLiveCoverage({ contract, fetchImpl, onOutcome: setOutcome }), [contract, fetchImpl]);
  return (
    <LiveAnswer>
      <CoverageAnswerView outcome={outcome} />
    </LiveAnswer>
  );
}
