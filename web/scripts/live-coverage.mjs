// The Trust and Coverage screen, asked of the live API.
//
// The first screen that reads a served answer instead of a fixture. It asks `coverage` with no
// parameters (the request carries no query text at all), through the one client module, and turns
// what comes back into one view state. Every rule about what a coverage answer may say stays in
// `coverage.mjs` (`readCoverage`), and every rule about a refusal in `refusal-card.mjs`; this file
// decides only which of them a state goes to, and the few sentences a page needs for the states
// that carry no answer.
//
// A served answer the coverage reader cannot account for is an `invalid_envelope`, never a page
// rendered from a shape it does not know: the reader was written against the platform's own
// sample, and an answer that no longer matches it is the drift this whole surface exists to catch.

import { askV3 } from "./v3-client.mjs";
import { readCoverage } from "./coverage.mjs";
import { validateRefusal } from "./refusal-card.mjs";
import { liveChrome } from "./live-chrome.mjs";

/**
 * The one sentence per refusal code a coverage request can meet. `coverage` takes no parameters,
 * so the only refusal a request from this page can receive is `no_corpus_mounted`; its sentence is
 * the refusal catalog's, and a test holds the two equal. Any other code gets a sentence that names
 * it rather than one written for a case this page cannot produce.
 */
export const LIVE_COVERAGE_REFUSAL_SENTENCES = Object.freeze({
  no_corpus_mounted: "This build has no index mounted.",
});

export const LIVE_COVERAGE_LOADING = liveChrome().coverage.loading;

/** The sentence for a refusal this page names only by its code. */
export function unexpectedRefusalSentence(code) {
  return `The coverage report was refused with ${code}.`;
}

/** The sentence for a transport failure, which carries the problem code the API sent below the envelope. */
export function transportFailureSentence(code) {
  return `The coverage report could not be reached (${code}).`;
}

/** The sentence for a refusal whose card the refusal card's own rules will not show. */
export function unshownRefusalSentence(code, reason) {
  return `The coverage report was refused with ${code}, and its card cannot be shown: ${reason}.`;
}

/** The sentence for an answer that is not one this page can read. */
export function invalidAnswerSentence(reason) {
  return `The answer could not be read as a coverage report: ${reason}.`;
}

/**
 * Maps what `askV3` returned to the view: `success` with the coverage answer (already read by
 * `readCoverage`), `refusal` with the refusal card's inputs, or a state that carries a sentence.
 */
export function coverageOutcome(asked) {
  if (asked.state === "success") {
    const answer = asked.envelope.result.value;
    try {
      readCoverage(answer);
    } catch (error) {
      return { state: "invalid_envelope", sentence: invalidAnswerSentence(error.message) };
    }
    return { state: "success", answer, context: asked.envelope.context };
  }

  if (asked.state === "refusal") {
    const { code, helpful_payload: payload } = asked.envelope.refusal;
    const sentence = LIVE_COVERAGE_REFUSAL_SENTENCES[code] ?? unexpectedRefusalSentence(code);
    // The card is checked here, before anything renders, as a success answer is checked by the
    // coverage reader: a refusal the card's rules refuse (a payload without the evidence the card
    // requires, a code whose card needs governing text) would otherwise throw during render and
    // leave no state on the page at all. It is still a refusal, said as one, without the card.
    try {
      validateRefusal({ code, sentence, payload });
    } catch (error) {
      return {
        state: "refusal",
        code,
        card: false,
        sentence: unshownRefusalSentence(code, error.message),
        context: asked.envelope.context,
      };
    }
    return { state: "refusal", code, card: true, sentence, payload, context: asked.envelope.context };
  }

  if (asked.state === "transport_failure") {
    return { state: "transport_failure", sentence: transportFailureSentence(asked.code) };
  }

  return { state: "invalid_envelope", sentence: invalidAnswerSentence(asked.reason) };
}

/** Asks the live API for its coverage report and returns the view state. */
export async function loadLiveCoverage({ contract, fetchImpl, signal }) {
  const asked = await askV3("coverage", {}, { contract, objectType: "coverage_report", fetchImpl, signal });
  return coverageOutcome(asked);
}

/**
 * What the screen does when it mounts: asks once, hands the view state to `onOutcome` when it
 * settles, and returns the cancel the screen calls when it unmounts. After cancel the request is
 * aborted and `onOutcome` is never called; a failure is handed over as a state, never retried.
 */
export function startLiveCoverage({ contract, fetchImpl, onOutcome }) {
  const controller = new AbortController();
  loadLiveCoverage({ contract, fetchImpl, signal: controller.signal })
    .then((next) => {
      if (!controller.signal.aborted) onOutcome(next);
    })
    .catch((error) => {
      if (error?.name !== "AbortError" && !controller.signal.aborted) {
        onOutcome({ state: "invalid_envelope", sentence: invalidAnswerSentence(String(error?.message ?? error)) });
      }
    });
  return () => controller.abort();
}
