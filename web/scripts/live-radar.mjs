// The change radar screen, asked of the live API.
//
// The seventh screen that reads a served answer. It asks `changes_in_period` with the two dates the
// reader typed and, when given, a work identifier and a language, through the one client module, and
// turns what comes back into one view state. Every rule about what a radar may say stays in
// `radar-answer.mjs` (`readChanges`), and every rule about a refusal in `refusal-card.mjs`; this file
// builds the request, decides which of them a state goes to, and holds the sentences a page needs for
// the states that carry no answer.

import { askV3 } from "./v3-client.mjs";
import { readChanges } from "./radar-answer.mjs";
import { validateRefusal } from "./refusal-card.mjs";
import { noCorpusMountedSentence } from "./live-refusals.mjs";
import { isCalendarDate } from "./temporal.mjs";
import { liveChrome } from "./live-chrome.mjs";

/**
 * The languages the form offers besides "any": the platform answers a language it holds no state in
 * with `language_not_available`, naming the ones it does.
 */
export const RADAR_LANGUAGES = Object.freeze([
  Object.freeze({ code: "fra", label: "French" }),
  Object.freeze({ code: "deu", label: "German" }),
  Object.freeze({ code: "eng", label: "English" }),
]);

/**
 * The one sentence per refusal code a request from this page can meet besides `no_corpus_mounted`,
 * whose sentence names the missing index from its payload. Any other code gets a sentence that names
 * it.
 */
export const LIVE_RADAR_REFUSAL_SENTENCES = Object.freeze({
  identifier_unknown: "This index holds no work under that identifier.",
  language_not_available: "This index holds no state in the language asked for.",
  retrieval_mode_unavailable: "This index cannot list this work's changes.",
});

export const LIVE_RADAR_IDLE = liveChrome().radar.idle;
export const LIVE_RADAR_LOADING = liveChrome().common.loading;

export function unexpectedRefusalSentence(code) {
  return `The change radar was refused with ${code}.`;
}

export function transportFailureSentence(code) {
  if (code === "request_schema_invalid") {
    return "This server refused the change radar request as it was asked (request_schema_invalid).";
  }
  return `The change radar could not be reached (${code}).`;
}

export function unshownRefusalSentence(code, reason) {
  return `The change radar was refused with ${code}, and its card cannot be shown: ${reason}.`;
}

export function invalidAnswerSentence(reason) {
  return `The answer could not be read as a change radar: ${reason}.`;
}

/**
 * The request parameters for one radar, checked before anything is sent: two calendar dates written
 * yyyy-mm-dd, a work identifier only when one is given (sent as typed), and a language only when one
 * is chosen. Nothing else is sent.
 */
export function radarParameters({ dateFrom, dateTo, identifier = "", language = "" }) {
  if (!isCalendarDate(dateFrom) || !isCalendarDate(dateTo)) {
    throw new Error("a change radar needs two calendar dates, written yyyy-mm-dd");
  }
  const parameters = { date_from: dateFrom, date_to: dateTo };
  if (typeof identifier !== "string") throw new Error("a work identifier is text");
  if (identifier.trim().length > 0) parameters.identifier = identifier;
  if (language !== "") {
    if (!RADAR_LANGUAGES.some((offered) => offered.code === language)) {
      throw new Error(`${JSON.stringify(language)} is not a language this form offers`);
    }
    parameters.language = language;
  }
  return parameters;
}

/**
 * Maps what `askV3` returned to the view: `success` with the radar view (read by `readChanges`),
 * `refusal` with the refusal card's inputs, or a state that carries a sentence.
 */
export function radarOutcome(asked) {
  if (asked.state === "success") {
    try {
      return { state: "success", view: readChanges(asked.envelope.result.value), context: asked.envelope.context };
    } catch (error) {
      return { state: "invalid_envelope", sentence: invalidAnswerSentence(error.message) };
    }
  }

  if (asked.state === "refusal") {
    const { code, helpful_payload: payload } = asked.envelope.refusal;
    const sentence = code === "no_corpus_mounted"
      ? noCorpusMountedSentence(payload)
      : LIVE_RADAR_REFUSAL_SENTENCES[code] ?? unexpectedRefusalSentence(code);
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

/** Asks the live API one change radar and returns the view state. */
export async function loadLiveRadar({ contract, fetchImpl, signal, request }) {
  const asked = await askV3("changes_in_period", radarParameters(request), { contract, objectType: "change_list", fetchImpl, signal });
  return radarOutcome(asked);
}

/**
 * One reader's radar requests on one screen: `ask` sends one (cancelling the one in flight) and
 * `cancel` abandons whatever is in flight. A request the form will not send is said as a state and
 * nothing is asked. After a cancel nothing settles.
 */
export function createRadarSession({ contract, fetchImpl, onOutcome }) {
  let controller = null;
  return {
    ask(request) {
      controller?.abort();
      controller = null;
      try {
        radarParameters(request);
      } catch (error) {
        onOutcome({ state: "invalid_request", sentence: `${error.message}.` });
        return false;
      }
      const own = new AbortController();
      controller = own;
      onOutcome({ state: "loading", sentence: LIVE_RADAR_LOADING });
      loadLiveRadar({ contract, fetchImpl, signal: own.signal, request })
        .then((next) => {
          if (!own.signal.aborted) onOutcome(next);
        })
        .catch((error) => {
          if (error?.name !== "AbortError" && !own.signal.aborted) {
            onOutcome({ state: "invalid_envelope", sentence: invalidAnswerSentence(String(error?.message ?? error)) });
          }
        });
      return true;
    },
    cancel() {
      controller?.abort();
      controller = null;
    },
  };
}
