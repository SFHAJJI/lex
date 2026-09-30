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
import { LIVE_COVERAGE_LOADING, loadLiveCoverage } from '../scripts/live-coverage.mjs';

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

  if (outcome.state === 'refusal') {
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
 * per mount and abandoned when the screen unmounts; a failure is shown, never retried behind the
 * reader's back.
 */
export function LiveCoverage({ contract, fetchImpl }) {
  const [outcome, setOutcome] = useState(LOADING);
  useEffect(() => {
    const controller = new AbortController();
    loadLiveCoverage({ contract, fetchImpl, signal: controller.signal })
      .then((next) => {
        if (!controller.signal.aborted) setOutcome(next);
      })
      .catch((error) => {
        if (error?.name !== 'AbortError') {
          setOutcome({ state: 'invalid_envelope', sentence: String(error?.message ?? error) });
        }
      });
    return () => controller.abort();
  }, [contract, fetchImpl]);
  return <CoverageAnswerView outcome={outcome} />;
}
